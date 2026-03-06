using System.Collections.Generic;
using UnityEngine;

namespace FuelDetector
{
    public class TrackedFuel
    {
        public int ID;
        public Vector2 Position; // In aspect-ratio corrected UV Space (Y: 0-1, X: 0-aspect)
        public Vector2 Velocity; // In aspect-ratio corrected UV Space
        public Vector2 PrevVelocity;
        public bool Counted;
        public int FramesSinceSeen;
        public int LifetimeFrames;
        public float Area; // In UV Space (0-1)
        public bool StartedInTopHalf;

        public bool isNotMovingUpward => (Velocity.y + PrevVelocity.y) < 0.06f;
        public bool isStationary => Velocity.sqrMagnitude < 0.001f;

        public TrackedFuel(int id, DetectedBlob blob, float midY)
        {
            ID = id;
            Position = blob.Centroid;
            Area = blob.Area;
            Counted = false;
            FramesSinceSeen = 0;
            LifetimeFrames = 1;
            PrevVelocity = Vector2.zero;

            // In Unity UV space, (0,0) is bottom-left. midY is in 0-1 range.
            StartedInTopHalf = Position.y > midY;

            // Estimate initial velocity if it starts in the top half
            if (StartedInTopHalf)
            {
                float radius = Mathf.Sqrt(Area / Mathf.PI);
                Velocity = new Vector2(Mathf.Cos(blob.Orientation), Mathf.Sin(blob.Orientation)) * radius;

                // Point the vector in a downward (y < 0) direction
                if (Velocity.y > 0) Velocity = -Velocity;
            }
            else
            {
                Velocity = Vector2.zero;
            }
        }
    }

    public class FuelTracker
    {
        public List<TrackedFuel> TrackedItems = new List<TrackedFuel>();
        private int _nextID = 0;

        public float MaxMatchDistance = 0.5f;
        public int MaxMissedFrames = 4;

        private readonly List<int> unmatchedBlobIndices = new();

        public int UpdateTracks(List<DetectedBlob> blobs, float midlineY)
        {
            int scoringCount = 0;

            unmatchedBlobIndices.Clear();
            for(int i=0; i < blobs.Count; i++)
                unmatchedBlobIndices.Add(i);

            float squaredMaxMatchDistance = MaxMatchDistance * MaxMatchDistance;

            // 2. Association
            for (int t = TrackedItems.Count - 1; t >= 0; t--)
            {
                var track = TrackedItems[t];
                track.FramesSinceSeen++;

                int bestMatchIdx = -1;
                float bestScore = float.MaxValue;

                // Predict next position (assuming 1 frame of movement)
                Vector2 trackPos = track.Position;
                Vector2 predictedPos = track.Position + track.Velocity;

                foreach (int i in unmatchedBlobIndices)
                {
                    // Look at 3 points between our current position and our estimated next position
                    Vector2 blobCentroid = blobs[i].Centroid;
                    float dist1 = (blobCentroid - trackPos).sqrMagnitude;
                    float dist2 = (blobCentroid - 0.5f * (trackPos + predictedPos)).sqrMagnitude;
                    float dist3 = (blobCentroid - predictedPos).sqrMagnitude;

                    float dist = Mathf.Min(dist1, dist2, dist3);

                    if (dist < squaredMaxMatchDistance && dist < bestScore)
                    {
                        bestScore = dist;
                        bestMatchIdx = i;
                    }
                }

                if (bestMatchIdx != -1)
                {
                    Vector2 newPos = blobs[bestMatchIdx].Centroid;
                    track.PrevVelocity = track.Velocity;
                    // Normalize velocity by the number of frames passed since it was last seen
                    track.Velocity = (newPos - track.Position) / track.FramesSinceSeen;
                    track.Position = newPos;
                    track.Area = blobs[bestMatchIdx].Area;
                    track.FramesSinceSeen = 0;
                    track.LifetimeFrames++;
                    unmatchedBlobIndices.Remove(bestMatchIdx);
                }
            }

            // 3. Remove Lost Tracks and Count them
            for (int i = TrackedItems.Count - 1; i >= 0; i--)
            {
                if (TrackedItems[i].FramesSinceSeen > MaxMissedFrames)
                {
                    var lostTrack = TrackedItems[i];

                    // Logic: Must have started in top half, seen for at least 2 frames, and not already counted.
                    if (!lostTrack.Counted && lostTrack.LifetimeFrames > 0 && lostTrack.StartedInTopHalf)
                    {
                        // Score if downward (-V) OR nearly stationary.
                        // Upward (+V) is considered a bounce and not counted.
                        if (lostTrack.isNotMovingUpward || lostTrack.isStationary)
                        {
                            scoringCount++;
                        }
                    }

                    TrackedItems.RemoveAt(i);
                }
            }

            // 4. Add new tracks
            foreach (int i in unmatchedBlobIndices)
            {
                TrackedItems.Add(new TrackedFuel(_nextID++, blobs[i], midlineY));
            }

            return scoringCount;
        }
    }
}