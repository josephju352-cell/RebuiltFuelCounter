using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class MDNSBroadcaster : MonoBehaviour
{
    [SerializeField] private string hostname = "FuelCounter"; // The desired hostname (e.g., FuelCounter.local)
    [SerializeField] private int servicePort = 8080;

    private UdpClient _udpClient;
    private Thread _listenThread;
    private bool _running;
    private IPAddress _localIP;
    
    // mDNS Multicast Address and Port
    private const string MulticastIP = "224.0.0.251";
    private const int MulticastPort = 5353;

#if UNITY_ANDROID && !UNITY_EDITOR
    private AndroidJavaObject multicastLock;
#endif

    private void Start()
    {
        _localIP = GetLocalIPAddress();
        if (_localIP == null)
        {
            Debug.LogError("[mDNS] Could not determine local IP address.");
            return;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        AcquireMulticastLock();
#endif
        StartResponder();
    }

    private void OnDestroy()
    {
        StopResponder();
#if UNITY_ANDROID && !UNITY_EDITOR
        ReleaseMulticastLock();
#endif
    }

    private IPAddress GetLocalIPAddress()
    {
        try
        {
            foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[mDNS] Error getting local IP: {e.Message}");
        }
        return null;
    }

    private void StartResponder()
    {
        try
        {
            _udpClient = new UdpClient();
            
            // Re-use address is critical for mDNS as multiple apps might listen on 5353
            _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            
            var localEp = new IPEndPoint(IPAddress.Any, MulticastPort);
            _udpClient.Client.Bind(localEp);

            // Join the multicast group
            _udpClient.JoinMulticastGroup(IPAddress.Parse(MulticastIP));

            _running = true;
            _listenThread = new Thread(ListenLoop);
            _listenThread.IsBackground = true;
            _listenThread.Start();

            Debug.Log($"[mDNS] Responder started. Hostname: {hostname}.local -> {_localIP} (Target Port: {servicePort})");
        }
        catch (Exception e)
        {
            Debug.LogError($"[mDNS] Failed to start responder: {e.Message}");
        }
    }

    private void StopResponder()
    {
        _running = false;
        if (_udpClient != null)
        {
            try { _udpClient.DropMulticastGroup(IPAddress.Parse(MulticastIP)); } catch {}
            _udpClient.Close();
            _udpClient = null;
        }
    }

    private void ListenLoop()
    {
        IPEndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);

        while (_running && _udpClient != null)
        {
            try
            {
                byte[] data = _udpClient.Receive(ref remoteEp);
                if (data != null && data.Length > 0)
                {
                    ParseAndRespond(data, remoteEp);
                }
            }
            catch (SocketException)
            {
                // Socket closed or error
                if (_running) Thread.Sleep(100);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[mDNS] Parse error: {e.Message}");
                if (_running) Thread.Sleep(100);
            }
        }
    }

    private void ParseAndRespond(byte[] query, IPEndPoint remoteEp)
    {
        // Very basic DNS packet parsing to find the Question
        // Header is 12 bytes.
        if (query.Length < 12) return;

        // Check if it's a query (QR bit 0 in flags at byte 2)
        // We only care about standard queries.
        
        int questionCount = (query[4] << 8) | query[5];
        if (questionCount <= 0) return;

        int currentPos = 12;

        for (int i = 0; i < questionCount; i++)
        {
            string qName = ParseName(query, ref currentPos);
            
            // QType (2 bytes) + QClass (2 bytes)
            if (currentPos + 4 > query.Length) return;
            
            // int qType = (query[currentPos] << 8) | query[currentPos + 1];
            // int qClass = (query[currentPos + 2] << 8) | query[currentPos + 3];
            currentPos += 4;

            // Check if they are asking for our hostname
            if (qName.Equals($"{hostname}.local", StringComparison.OrdinalIgnoreCase))
            {
                SendResponse();
                return; // We responded, no need to process other questions
            }
        }
    }

    private string ParseName(byte[] packet, ref int offset)
    {
        StringBuilder name = new StringBuilder();
        // Basic label parsing, handles pointers not strictly necessary for simple query parsing usually, but safe to include basic loop
        while (offset < packet.Length)
        {
            byte len = packet[offset++];
            if (len == 0) break; // End of name

            if ((len & 0xC0) == 0xC0) // Compression pointer (shouldn't happen in simple query question, but standard DNS)
            {
                offset++; // Skip next byte of pointer
                break; 
            }

            if (name.Length > 0) name.Append(".");
            
            if (offset + len > packet.Length) break;
            
            for (int i = 0; i < len; i++)
            {
                name.Append((char)packet[offset++]);
            }
        }
        return name.ToString();
    }

    private void SendResponse()
    {
        // Construct a simple DNS A-Record Response
        // Transaction ID: 0 (or match query, but 0 often accepted for mDNS broadcasts)
        // Flags: 0x8400 (Response, Authoritative)
        
        List<byte> response = new List<byte>();

        // Header
        response.AddRange(new byte[] { 0x00, 0x00 }); // ID
        response.AddRange(new byte[] { 0x84, 0x00 }); // Flags (Response, Auth)
        response.AddRange(new byte[] { 0x00, 0x00 }); // QDCOUNT (Questions)
        response.AddRange(new byte[] { 0x00, 0x01 }); // ANCOUNT (Answers: 1)
        response.AddRange(new byte[] { 0x00, 0x00 }); // NSCOUNT
        response.AddRange(new byte[] { 0x00, 0x00 }); // ARCOUNT

        // Answer Section
        // Name: "FuelCounter.local" (compressed or full)
        // Let's do full labels: 11 "FuelCounter" 5 "local" 0
        AddDomainName(response, $"{hostname}.local");

        // Type: A (1)
        response.AddRange(new byte[] { 0x00, 0x01 });
        // Class: IN (1) | Cache Flush (0x8000) -> 0x8001
        response.AddRange(new byte[] { 0x00, 0x01 }); 
        // TTL: 120 seconds
        response.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x78 });
        // RDLENGTH: 4 (IPv4)
        response.AddRange(new byte[] { 0x00, 0x04 });
        // RDATA: IP Bytes
        response.AddRange(_localIP.GetAddressBytes());

        byte[] packet = response.ToArray();
        
        // Send to Multicast Group
        IPEndPoint multicastEp = new IPEndPoint(IPAddress.Parse(MulticastIP), MulticastPort);
        _udpClient.Send(packet, packet.Length, multicastEp);
        
        Debug.Log($"[mDNS] Sent response for {hostname}.local");
    }

    private void AddDomainName(List<byte> buffer, string domain)
    {
        var parts = domain.Split('.');
        foreach (var part in parts)
        {
            buffer.Add((byte)part.Length);
            foreach (char c in part) buffer.Add((byte)c);
        }
        buffer.Add(0); // Root
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private void AcquireMulticastLock()
    {
        try
        {
            AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            AndroidJavaObject context = currentActivity.Call<AndroidJavaObject>("getApplicationContext");
            
            string WIFI_SERVICE = "wifi";
            AndroidJavaObject wifiManager = context.Call<AndroidJavaObject>("getSystemService", WIFI_SERVICE);
            
            multicastLock = wifiManager.Call<AndroidJavaObject>("createMulticastLock", "FuelCounterMulticastLock");
            
            if (multicastLock != null)
            {
                multicastLock.Call("setReferenceCounted", true);
                multicastLock.Call("acquire");
                Debug.Log("Multicast lock acquired");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to acquire multicast lock: {e.Message}");
        }
    }

    private void ReleaseMulticastLock()
    {
        if (multicastLock != null)
        {
            try
            {
                if (multicastLock.Call<bool>("isHeld"))
                {
                    multicastLock.Call("release");
                    Debug.Log("Multicast lock released");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error releasing multicast lock: {e.Message}");
            }
            finally
            {
                multicastLock = null;
            }
        }
    }
#endif
}