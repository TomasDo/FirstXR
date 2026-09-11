using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// CPU classifier for one downscaled RGB frame: skin mask → largest blob → contour /
    /// convexity defects → open palm, fist, or pinch.
    /// </summary>
    public sealed class RgbHandGestureAnalyzer
    {
        const float MinAreaRatio = 0.025f;
        const float MaxAreaRatio = 0.72f;
        const int MinContourPoints = 16;
        const float DefectDepthRatio = 0.12f;
        const float DefectMaxAngle = 95f;
        const float PeakRadiusRatio = 0.42f;
        const float PinchPeakSeparation = 0.30f;
        const float FistCompactness = 0.56f;

        static readonly Vector2Int[] MooreClockwise =
        {
            new Vector2Int(0, -1),
            new Vector2Int(1, -1),
            new Vector2Int(1, 0),
            new Vector2Int(1, 1),
            new Vector2Int(0, 1),
            new Vector2Int(-1, 1),
            new Vector2Int(-1, 0),
            new Vector2Int(-1, -1),
        };

        byte[] m_Mask;
        byte[] m_Scratch;
        byte[] m_Visited;
        int[] m_Parent;
        int[] m_Size;
        int[] m_Stack;
        readonly List<Vector2Int> m_Contour = new List<Vector2Int>(512);
        readonly List<Vector2Int> m_Hull = new List<Vector2Int>(64);
        readonly List<Vector2Int> m_HullOrdered = new List<Vector2Int>(64);
        readonly List<Vector2Int> m_Peaks = new List<Vector2Int>(8);
        readonly List<Vector2Int> m_SortBuffer = new List<Vector2Int>(64);
        readonly HashSet<Vector2Int> m_HullSet = new HashSet<Vector2Int>();

        int m_Width;
        int m_Height;

        public RgbHandGestureObservation Analyze(Color32[] pixels, int width, int height)
        {
            if (pixels == null || width < 16 || height < 16 || pixels.Length < width * height)
                return RgbHandGestureObservation.None;

            EnsureBuffers(width, height);
            BuildSkinMask(pixels, width, height);
            CloseMask(width, height);
            OpenMask(width, height);

            var largestRoot = FindLargestComponent(width, height, out var area);
            var pixelCount = width * height;
            var areaRatio = area / (float)pixelCount;
            if (largestRoot < 0 || areaRatio < MinAreaRatio || areaRatio > MaxAreaRatio)
                return RgbHandGestureObservation.None;

            IsolateComponent(width, height, largestRoot);
            if (!TryTraceContour(width, height))
                return RgbHandGestureObservation.None;

            var compactness = ComputeCompactness(area, m_Contour.Count);
            var hasHole = DetectHole(width, height, area);
            BuildConvexHull();
            OrderHullAlongContour();

            var bboxHeight = ComputeBBoxHeight();
            var defectCount = CountConvexityDefects(bboxHeight);
            var centroid = ComputeCentroid();
            var maxRadius = CollectPeaks(centroid);
            var peakSeparation = ComputePeakSeparation(maxRadius);

            var observation = new RgbHandGestureObservation
            {
                HandDetected = true,
                DefectCount = defectCount,
                PeakCount = m_Peaks.Count,
                Compactness = compactness,
                AreaRatio = areaRatio,
                PeakSeparation = peakSeparation,
                HasHole = hasHole,
            };
            observation.Gesture = Classify(observation);
            return observation;
        }

        void EnsureBuffers(int width, int height)
        {
            var count = width * height;
            if (m_Mask != null && m_Mask.Length >= count && m_Width == width && m_Height == height)
                return;

            m_Width = width;
            m_Height = height;
            m_Mask = new byte[count];
            m_Scratch = new byte[count];
            m_Visited = new byte[count];
            m_Parent = new int[count];
            m_Size = new int[count];
            m_Stack = new int[count];
        }

        void BuildSkinMask(Color32[] pixels, int width, int height)
        {
            var count = width * height;
            for (var i = 0; i < count; i++)
            {
                var c = pixels[i];
                var r = LinearToSrgb(c.r);
                var g = LinearToSrgb(c.g);
                var b = LinearToSrgb(c.b);
                m_Mask[i] = IsSkin(r, g, b) ? (byte)1 : (byte)0;
            }
        }

        static byte LinearToSrgb(byte value)
        {
            var x = value / 255f;
            var gamma = Mathf.LinearToGammaSpace(x);
            return (byte)Mathf.Clamp(Mathf.RoundToInt(gamma * 255f), 0, 255);
        }

        static bool IsSkin(int r, int g, int b)
        {
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            if (max < 40 || max - min < 12)
                return false;

            return MatchesSkin(r, g, b) || MatchesSkin(b, g, r);
        }

        static bool MatchesSkin(int r, int g, int b)
        {
            var y = (r * 299 + g * 587 + b * 114) / 1000;
            var cb = 128 + (-169 * r - 331 * g + 500 * b) / 1000;
            var cr = 128 + (500 * r - 419 * g - 81 * b) / 1000;
            var ycbcr = y > 40 && cb >= 70 && cb <= 140 && cr >= 125 && cr <= 185;
            var kovac = r > 90 && g > 35 && b > 20 && r > g && r > b && (r - g) > 12;
            return ycbcr || kovac;
        }

        void CloseMask(int width, int height)
        {
            Dilate(width, height);
            Array.Copy(m_Scratch, m_Mask, width * height);
            Erode(width, height);
            Array.Copy(m_Scratch, m_Mask, width * height);
        }

        void OpenMask(int width, int height)
        {
            Erode(width, height);
            Array.Copy(m_Scratch, m_Mask, width * height);
            Dilate(width, height);
            Array.Copy(m_Scratch, m_Mask, width * height);
        }

        void Dilate(int width, int height)
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var on = false;
                    for (var dy = -1; dy <= 1 && !on; dy++)
                    {
                        var ny = y + dy;
                        if ((uint)ny >= (uint)height)
                            continue;
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            var nx = x + dx;
                            if ((uint)nx >= (uint)width)
                                continue;
                            if (m_Mask[ny * width + nx] != 0)
                            {
                                on = true;
                                break;
                            }
                        }
                    }

                    m_Scratch[y * width + x] = on ? (byte)1 : (byte)0;
                }
            }
        }

        void Erode(int width, int height)
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var on = true;
                    for (var dy = -1; dy <= 1 && on; dy++)
                    {
                        var ny = y + dy;
                        if ((uint)ny >= (uint)height)
                        {
                            on = false;
                            break;
                        }

                        for (var dx = -1; dx <= 1; dx++)
                        {
                            var nx = x + dx;
                            if ((uint)nx >= (uint)width || m_Mask[ny * width + nx] == 0)
                            {
                                on = false;
                                break;
                            }
                        }
                    }

                    m_Scratch[y * width + x] = on ? (byte)1 : (byte)0;
                }
            }
        }

        int FindLargestComponent(int width, int height, out int largestArea)
        {
            var count = width * height;
            for (var i = 0; i < count; i++)
            {
                m_Parent[i] = i;
                m_Size[i] = m_Mask[i] != 0 ? 1 : 0;
            }

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var i = y * width + x;
                    if (m_Mask[i] == 0)
                        continue;

                    if (x + 1 < width && m_Mask[i + 1] != 0)
                        Union(i, i + 1);
                    if (y + 1 < height && m_Mask[i + width] != 0)
                        Union(i, i + width);
                }
            }

            var bestRoot = -1;
            largestArea = 0;
            for (var i = 0; i < count; i++)
            {
                if (m_Mask[i] == 0)
                    continue;
                var root = Find(i);
                if (m_Size[root] > largestArea)
                {
                    largestArea = m_Size[root];
                    bestRoot = root;
                }
            }

            return bestRoot;
        }

        int Find(int x)
        {
            while (m_Parent[x] != x)
            {
                m_Parent[x] = m_Parent[m_Parent[x]];
                x = m_Parent[x];
            }

            return x;
        }

        void Union(int a, int b)
        {
            a = Find(a);
            b = Find(b);
            if (a == b)
                return;

            if (m_Size[a] < m_Size[b])
            {
                var tmp = a;
                a = b;
                b = tmp;
            }

            m_Parent[b] = a;
            m_Size[a] += m_Size[b];
        }

        void IsolateComponent(int width, int height, int root)
        {
            var count = width * height;
            for (var i = 0; i < count; i++)
            {
                if (m_Mask[i] == 0)
                    continue;
                m_Mask[i] = Find(i) == root ? (byte)1 : (byte)0;
            }
        }

        bool TryTraceContour(int width, int height)
        {
            m_Contour.Clear();
            var start = -1;
            var count = width * height;
            for (var i = 0; i < count; i++)
            {
                if (m_Mask[i] != 0)
                {
                    start = i;
                    break;
                }
            }

            if (start < 0)
                return false;

            var startX = start % width;
            var startY = start / width;
            var x = startX;
            var y = startY;
            var backDir = 6;

            for (var step = 0; step < count; step++)
            {
                m_Contour.Add(new Vector2Int(x, y));
                var found = false;
                var nextDir = 0;
                var search = (backDir + 6) % 8;
                for (var k = 0; k < 8; k++)
                {
                    var dir = (search + k) % 8;
                    var nx = x + MooreClockwise[dir].x;
                    var ny = y + MooreClockwise[dir].y;
                    if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
                        continue;
                    if (m_Mask[ny * width + nx] == 0)
                        continue;

                    x = nx;
                    y = ny;
                    nextDir = dir;
                    found = true;
                    break;
                }

                if (!found)
                    break;

                backDir = nextDir;
                if (x == startX && y == startY && m_Contour.Count > 2)
                    break;
            }

            return m_Contour.Count >= MinContourPoints;
        }

        static float ComputeCompactness(int area, int perimeter)
        {
            if (perimeter < 4)
                return 0f;
            return 4f * Mathf.PI * area / (perimeter * perimeter);
        }

        bool DetectHole(int width, int height, int handArea)
        {
            var count = width * height;
            Array.Clear(m_Visited, 0, count);
            var stackTop = 0;

            void PushIfBackground(int x, int y)
            {
                if ((uint)x >= (uint)width || (uint)y >= (uint)height)
                    return;
                var i = y * width + x;
                if (m_Mask[i] != 0 || m_Visited[i] != 0)
                    return;
                m_Visited[i] = 1;
                m_Stack[stackTop++] = i;
            }

            for (var x = 0; x < width; x++)
            {
                PushIfBackground(x, 0);
                PushIfBackground(x, height - 1);
            }

            for (var y = 0; y < height; y++)
            {
                PushIfBackground(0, y);
                PushIfBackground(width - 1, y);
            }

            while (stackTop > 0)
            {
                var i = m_Stack[--stackTop];
                var x = i % width;
                var y = i / width;
                PushIfBackground(x - 1, y);
                PushIfBackground(x + 1, y);
                PushIfBackground(x, y - 1);
                PushIfBackground(x, y + 1);
            }

            var holePixels = 0;
            for (var i = 0; i < count; i++)
            {
                if (m_Mask[i] == 0 && m_Visited[i] == 0)
                    holePixels++;
            }

            var holeRatio = holePixels / (float)Math.Max(1, handArea);
            return holePixels >= 12 && holeRatio >= 0.02f && holeRatio <= 0.35f;
        }

        void BuildConvexHull()
        {
            m_SortBuffer.Clear();
            for (var i = 0; i < m_Contour.Count; i++)
                m_SortBuffer.Add(m_Contour[i]);

            m_SortBuffer.Sort((a, b) =>
            {
                var cx = a.x.CompareTo(b.x);
                return cx != 0 ? cx : a.y.CompareTo(b.y);
            });

            m_Hull.Clear();
            if (m_SortBuffer.Count < 3)
            {
                m_Hull.AddRange(m_SortBuffer);
                return;
            }

            foreach (var p in m_SortBuffer)
            {
                while (m_Hull.Count >= 2 && Cross(m_Hull[m_Hull.Count - 2], m_Hull[m_Hull.Count - 1], p) <= 0)
                    m_Hull.RemoveAt(m_Hull.Count - 1);
                m_Hull.Add(p);
            }

            var lowerCount = m_Hull.Count + 1;
            for (var i = m_SortBuffer.Count - 2; i >= 0; i--)
            {
                var p = m_SortBuffer[i];
                while (m_Hull.Count >= lowerCount && Cross(m_Hull[m_Hull.Count - 2], m_Hull[m_Hull.Count - 1], p) <= 0)
                    m_Hull.RemoveAt(m_Hull.Count - 1);
                m_Hull.Add(p);
            }

            if (m_Hull.Count > 1)
                m_Hull.RemoveAt(m_Hull.Count - 1);
        }

        static int Cross(Vector2Int o, Vector2Int a, Vector2Int b)
        {
            return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        }

        void OrderHullAlongContour()
        {
            m_HullOrdered.Clear();
            if (m_Hull.Count == 0)
                return;

            m_HullSet.Clear();
            for (var i = 0; i < m_Hull.Count; i++)
                m_HullSet.Add(m_Hull[i]);
            for (var i = 0; i < m_Contour.Count; i++)
            {
                var p = m_Contour[i];
                if (m_HullSet.Contains(p))
                    m_HullOrdered.Add(p);
            }

            if (m_HullOrdered.Count < 3)
            {
                m_HullOrdered.Clear();
                m_HullOrdered.AddRange(m_Hull);
            }
        }

        int ComputeBBoxHeight()
        {
            var minY = int.MaxValue;
            var maxY = int.MinValue;
            for (var i = 0; i < m_Contour.Count; i++)
            {
                var y = m_Contour[i].y;
                if (y < minY)
                    minY = y;
                if (y > maxY)
                    maxY = y;
            }

            return Math.Max(1, maxY - minY);
        }

        int CountConvexityDefects(int bboxHeight)
        {
            if (m_HullOrdered.Count < 3 || m_Contour.Count < 8)
                return 0;

            var minDepth = Math.Max(3f, bboxHeight * DefectDepthRatio);
            var defects = 0;
            var contourCount = m_Contour.Count;

            int FindIndex(Vector2Int p)
            {
                for (var i = 0; i < contourCount; i++)
                {
                    if (m_Contour[i] == p)
                        return i;
                }

                return 0;
            }

            for (var h = 0; h < m_HullOrdered.Count; h++)
            {
                var start = m_HullOrdered[h];
                var end = m_HullOrdered[(h + 1) % m_HullOrdered.Count];
                var startIndex = FindIndex(start);
                var endIndex = FindIndex(end);
                if (startIndex == endIndex)
                    continue;

                var maxDepth = 0f;
                var depthPoint = start;
                for (var i = (startIndex + 1) % contourCount; i != endIndex; i = (i + 1) % contourCount)
                {
                    var p = m_Contour[i];
                    var depth = DistanceToLine(start, end, p);
                    if (depth > maxDepth)
                    {
                        maxDepth = depth;
                        depthPoint = p;
                    }
                }

                if (maxDepth < minDepth)
                    continue;

                var angle = Vector2.Angle(
                    (Vector2)(start - depthPoint),
                    (Vector2)(end - depthPoint));
                if (angle > 0f && angle < DefectMaxAngle)
                    defects++;
            }

            return defects;
        }

        static float DistanceToLine(Vector2Int a, Vector2Int b, Vector2Int p)
        {
            var abx = b.x - a.x;
            var aby = b.y - a.y;
            var len2 = abx * abx + aby * aby;
            if (len2 < 1)
                return Vector2Int.Distance(p, a);

            var t = ((p.x - a.x) * abx + (p.y - a.y) * aby) / (float)len2;
            t = Mathf.Clamp01(t);
            var px = a.x + t * abx;
            var py = a.y + t * aby;
            var dx = p.x - px;
            var dy = p.y - py;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        Vector2 ComputeCentroid()
        {
            var sx = 0f;
            var sy = 0f;
            for (var i = 0; i < m_Contour.Count; i++)
            {
                sx += m_Contour[i].x;
                sy += m_Contour[i].y;
            }

            return new Vector2(sx / m_Contour.Count, sy / m_Contour.Count);
        }

        float CollectPeaks(Vector2 centroid)
        {
            m_Peaks.Clear();
            if (m_HullOrdered.Count < 3)
                return 0f;

            var distances = new float[m_HullOrdered.Count];
            var maxRadius = 0.01f;
            for (var i = 0; i < m_HullOrdered.Count; i++)
            {
                distances[i] = Vector2.Distance((Vector2)m_HullOrdered[i], centroid);
                if (distances[i] > maxRadius)
                    maxRadius = distances[i];
            }

            var threshold = maxRadius * PeakRadiusRatio;
            for (var i = 0; i < m_HullOrdered.Count; i++)
            {
                var prev = distances[(i + m_HullOrdered.Count - 1) % m_HullOrdered.Count];
                var next = distances[(i + 1) % m_HullOrdered.Count];
                if (distances[i] >= prev && distances[i] >= next && distances[i] >= threshold)
                    m_Peaks.Add(m_HullOrdered[i]);
            }

            return maxRadius;
        }

        float ComputePeakSeparation(float maxRadius)
        {
            if (m_Peaks.Count < 2 || maxRadius < 1f)
                return 1f;

            var minSep = float.MaxValue;
            for (var i = 0; i < m_Peaks.Count; i++)
            {
                for (var j = i + 1; j < m_Peaks.Count; j++)
                {
                    var sep = Vector2Int.Distance(m_Peaks[i], m_Peaks[j]) / maxRadius;
                    if (sep < minSep)
                        minSep = sep;
                }
            }

            return minSep;
        }

        static RgbHandGesture Classify(RgbHandGestureObservation o)
        {
            if (!o.HandDetected)
                return RgbHandGesture.None;

            if (o.HasHole && o.Compactness < 0.78f)
                return RgbHandGesture.Pinch;

            if (o.DefectCount >= 3 || o.PeakCount >= 4)
                return RgbHandGesture.OpenPalm;

            if (o.PeakCount == 2 && o.PeakSeparation < PinchPeakSeparation)
                return RgbHandGesture.Pinch;

            if (o.DefectCount == 1 && o.PeakCount <= 3 && o.PeakSeparation < 0.36f && o.Compactness < 0.64f)
                return RgbHandGesture.Pinch;

            if (o.Compactness >= FistCompactness && o.DefectCount <= 1 && o.PeakCount <= 2)
                return RgbHandGesture.Fist;

            if (o.DefectCount == 2 && o.Compactness < 0.55f)
                return RgbHandGesture.OpenPalm;

            if (o.Compactness >= 0.50f && o.PeakCount <= 1)
                return RgbHandGesture.Fist;

            return RgbHandGesture.None;
        }
    }
}
