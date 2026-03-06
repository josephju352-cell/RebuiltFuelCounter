using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Assertions;

namespace FuelDetector
{
    public class DetectedBlob
    {
        public Vector2 Centroid; // In aspect-ratio corrected UV space (Y is 0-1, X is 0-aspect)
        public float Orientation; // In Radians
        public float Area; // In UV Space (0-1)
    }

    public class BlobProcessor
    {
        List<DetectedBlob> blobs = new List<DetectedBlob>(8);
        byte[] visited;

        public List<DetectedBlob> Process(NativeArray<byte> data, int width, int height, float sourceAspect, float minArea)
        {
            blobs.Clear();
            int length = data.Length;

            // Assuming R8 (1 byte) or RGBA32 (4 bytes).
            // We check the first byte (R) for the binary mask.
            int pixelStride = (length == width * height) ? 1 : 4;
            if (visited == null || visited.Length != width * height)
                visited = new byte[width * height];
            Array.Clear(visited, 0, visited.Length);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    if (visited[index] != 0) continue;
                    visited[index] = 1;

                    int byteIndex = index * pixelStride;
                    if (data[byteIndex] > 128) // Threshold for "White"
                    {
                        DetectedBlob blob = FloodFill(data, visited, width, height, x, y, pixelStride, sourceAspect, minArea);
                        if (blob != null)
                            blobs.Add(blob);
                    }
                }
            }

            return blobs;
        }

        Stack<ushort> stack = new Stack<ushort>(1024);

		// Directions to flood fill
		static readonly int[] neighbor_dx = { 0, 0, 1, -1 };
		static readonly int[] neighbor_dy = { 1, -1, 0, 0 };

        private DetectedBlob FloodFill(NativeArray<byte> data, byte[] visited, int width, int height, int startX, int startY, int pixelStride, float sourceAspect, float minArea)
        {
            Assert.IsTrue(width <= 65535 && height <= 65535, "'stack' is not made to handle large textures!");
            Assert.IsTrue(startX >= 0 && startX < width, "Invalid startX passed!");
            Assert.IsTrue(startY >= 0 && startY < height, "Invalid startY passed!");

            int minCount = Mathf.CeilToInt(minArea * (width * height) / sourceAspect);

            stack.Clear();
            stack.Push((ushort)startX);
            stack.Push((ushort)startY);

            double sum_x = 0;
            double sum_y = 0;
            double sum_xx = 0;
            double sum_yy = 0;
            double sum_xy = 0;
            int count = 0;

            while(stack.TryPop(out ushort usy))
            {
                int ix = stack.Pop();
                int iy = usy;
                double fx = ix;
                double fy = iy;
                sum_x += fx;
                sum_y += fy;
                sum_xx += fx * fx;
                sum_yy += fy * fy;
                sum_xy += fx * fy;
                count++;

                for (int i = 0; i < 4; i++)
                {
                    int nx = ix + neighbor_dx[i];
                    int ny = iy + neighbor_dy[i];

                    if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                    {
                        int nIndex = ny * width + nx;
                        if (visited[nIndex] == 0)
                        {
                            if (data[nIndex * pixelStride] > 128)
                            {
                                visited[nIndex] = 1;
                                stack.Push((ushort)nx);
                                stack.Push((ushort)ny);
                            }
                        }
                    }
                }
            }

            // Does this have enough pixels?
            if (count < minCount)
                return null;

            DetectedBlob blob = new DetectedBlob();
            // Convert pixel count to aspect-corrected UV area
            blob.Area = (float)count * sourceAspect / (width * height);

            // Spatially correct coordinate system: Y is 0-1, X is 0-sourceAspect
            float invWidth = 1.0f / width;
            float invHeight = 1.0f / height;
            float cx = (float)(sum_x / count) * invWidth * sourceAspect;
            float cy = (float)(sum_y / count) * invHeight;
            blob.Centroid = new Vector2(cx, cy);

            // Calculate central moments for orientation in the same space
            double xFactor = (double)sourceAspect * invWidth;
            double yFactor = invHeight;

            double mu20 = (sum_xx * xFactor * xFactor / count) - (cx * cx);
            double mu02 = (sum_yy * yFactor * yFactor / count) - (cy * cy);
            double mu11 = (sum_xy * xFactor * yFactor / count) - (cx * cy);

            blob.Orientation = 0.5f * Mathf.Atan2(2 * (float)mu11, (float)(mu20 - mu02));

            return blob;
        }
    }
}

