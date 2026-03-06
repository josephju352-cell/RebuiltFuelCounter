using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;

public class WebServerManager : MonoBehaviour
{
    [SerializeField] private int port = 8080;
    [SerializeField] private TextMeshProUGUI logText;
    [SerializeField] private int maxLogLines = 20;

    private HttpListener _listener;
    private Thread _listenerThread;
    private readonly Queue<string> _logQueue = new Queue<string>();
    private readonly object _logLock = new object();
    private bool _running = false;
    private string _webRoot;
    private readonly List<string> _logLines = new List<string>();

    private void Start()
    {
        _webRoot = Path.Combine(Application.persistentDataPath, "WebServer");
        logText.text = string.Empty;
        StartCoroutine(InitializeWebFilesAndStartServer());
    }

    private void OnDestroy()
    {
        StopServer();
    }

    private void Update()
    {
        bool changed = false;
        lock (_logLock)
        {
            while (_logQueue.Count > 0)
            {
                string message = _logQueue.Dequeue();
                AddLogLine(message);
                changed = true;
            }
        }

        if (changed)
        {
            UpdateLogUI();
        }
    }

    private IEnumerator InitializeWebFilesAndStartServer()
    {
        if (!Directory.Exists(_webRoot))
            Directory.CreateDirectory(_webRoot);

        string[] filesToCopy = new string[]
        {
            "index.html",
            "css/style.css",
            "js/app.js"
        };

        foreach (string relativePath in filesToCopy)
        {
            string sourcePath = Path.Combine(Application.streamingAssetsPath, "WebServer", relativePath);
            if (Application.platform != RuntimePlatform.Android) 
                sourcePath = "file://" + sourcePath;

            using (UnityWebRequest www = UnityWebRequest.Get(sourcePath))
            {
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    string destPath = Path.Combine(_webRoot, relativePath);
                    string destDir = Path.GetDirectoryName(destPath);
                    if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                    File.WriteAllBytes(destPath, www.downloadHandler.data);
                }
            }
        }

        StartServer();
    }

    private void StartServer()
    {
        if (_running) return;
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://*:{port}/");
            _listener.Start();
            _running = true;
            _listenerThread = new Thread(ListenLoop) { IsBackground = true };
            _listenerThread.Start();
            Log($"Server started on port {port}");
        }
        catch (Exception e) { Log($"Error: {e.Message}"); }
    }

    private void StopServer()
    {
        _running = false;
        _listener?.Stop();
        _listener?.Close();
        _listener = null;
    }

    private void ListenLoop()
    {
        while (_running && _listener != null && _listener.IsListening)
        {
            try
            {
                var context = _listener.GetContext();
                ThreadPool.QueueUserWorkItem((o) => HandleRequest(context));
            }
            catch (Exception)
            {
                // Listener stopped or other error. 
                // Add a small sleep to prevent tight-looping if the error is persistent.
                if (_running) Thread.Sleep(100);
            }
        }
    }

    private void HandleRequest(HttpListenerContext context)
    {
        try
        {
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;
            string url = request.Url.AbsolutePath;
            string clientIp = request.RemoteEndPoint.Address.ToString();

            if (url != "/api/status")
            {
                QueueLog($"[{request.HttpMethod}] [{clientIp}] {url}");
            }

            if (url.StartsWith("/api/"))
            {
                HandleApiRequest(request, response);
                return;
            }

            if (url == "/") url = "/index.html";
            string filePath = Path.Combine(_webRoot, url.TrimStart('/'));

            if (File.Exists(filePath)) ServeFile(filePath, response);
            else { response.StatusCode = 404; CloseResponse(response, "Not Found"); }
        }
        catch (Exception e) { QueueLog($"Request error: {e.Message}"); }
    }

    private void HandleApiRequest(HttpListenerRequest request, HttpListenerResponse response)
    {
        string url = request.Url.AbsolutePath;
        string method = request.HttpMethod;
        string jsonResponse = "";
        response.ContentType = "application/json";

        if (url == "/api/status" && method == "GET")
        {
            var fc = FuelCounter.Instance;
            if (fc != null)
            {
                string units = fc.DisplayPerMinute ? "min" : "sec";
                jsonResponse = $"{{\"count\":{fc.TotalFuelCount},\"rate\":{fc.GetRatePerMinute():F2},\"uptime\":\"{fc.GetElapsedTime()}\",\"units\":\"{units}\",\"muted\":{fc.IsMuted.ToString().ToLower()}}}";
            }
            else jsonResponse = "{{\"error\":\"FuelCounter not found\"}}";
        }
        else if (url == "/api/resetCount" && method == "POST")
        {
            FuelCounter.Instance?.ResetCount();
            jsonResponse = "{\"status\":\"count reset\"}";
        }
        else if (url == "/api/resetTimer" && method == "POST")
        {
            FuelCounter.Instance?.ResetTimer();
            jsonResponse = "{\"status\":\"timer reset\"}";
        }
        else if (url == "/api/setMute" && method == "POST")
        {
            string muteParam = request.QueryString["mute"];
            if (!string.IsNullOrEmpty(muteParam))
            {
                bool shouldMute = muteParam.ToLower() == "true";
                FuelCounter.Instance?.SetMute(shouldMute);
                jsonResponse = $"{{\"muted\":{shouldMute.ToString().ToLower()}}}";
            }
            else jsonResponse = "{\"error\":\"Missing 'mute' parameter\"}";
        }
        else
        {
            response.StatusCode = 404;
            jsonResponse = "{\"error\":\"Endpoint not found\"}";
        }

        byte[] buffer = Encoding.UTF8.GetBytes(jsonResponse);
        response.ContentLength64 = buffer.Length;
        response.OutputStream.Write(buffer, 0, buffer.Length);
        response.OutputStream.Close();
    }

    private void ServeFile(string filePath, HttpListenerResponse response)
    {
        byte[] buffer = File.ReadAllBytes(filePath);
        response.ContentType = GetContentType(Path.GetExtension(filePath).ToLower());
        response.ContentLength64 = buffer.Length;
        response.OutputStream.Write(buffer, 0, buffer.Length);
        response.OutputStream.Close();
    }

    private string GetContentType(string ext) => ext switch {
        ".html" => "text/html",
        ".css" => "text/css",
        ".js" => "application/javascript",
        _ => "application/octet-stream"
    };

    private void CloseResponse(HttpListenerResponse res, string msg)
    {
        byte[] b = Encoding.UTF8.GetBytes(msg);
        res.ContentLength64 = b.Length;
        res.OutputStream.Write(b, 0, b.Length);
        res.OutputStream.Close();
    }

    private void QueueLog(string m) { lock (_logLock) _logQueue.Enqueue(m); }
    private void Log(string m) 
    { 
        if (Thread.CurrentThread.ManagedThreadId == 1) 
        {
            AddLogLine(m);
            UpdateLogUI();
        }
        else 
        {
            QueueLog(m); 
        }
    }

    private void AddLogLine(string m)
    {
        _logLines.Add($"[{DateTime.Now:HH:mm:ss}] {m}");
        if (_logLines.Count > maxLogLines)
            _logLines.RemoveAt(0);
    }

    private void UpdateLogUI()
    {
        if (!logText) return;
        
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < _logLines.Count; i++)
        {
            sb.AppendLine(_logLines[i]);
        }
        logText.text = sb.ToString();
    }
}
