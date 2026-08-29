using System;
using System.Text;

namespace VpbNet
{
    public enum VpbNetSampleState
    {
        Empty = 0,
        Interpolated = 1,
        Extrapolated = 2,
        Frozen = 3
    }

    public sealed class VpbNetSnapshotBuffer
    {
        public const int Capacity = 32;
        public const double MaxExtrapolateMs = 150.0;
        public const double FutureLimitMs = 5000.0;

        readonly float[] _poses = new float[Capacity * VpbPose.PoseFloats];
        readonly uint[] _seq = new uint[Capacity];
        readonly double[] _ms = new double[Capacity];
        readonly int[] _order = new int[Capacity];
        readonly bool[] _used = new bool[Capacity];

        int _count;
        int _inserted;
        int _rejectedDuplicate;
        int _rejectedAncient;
        int _rejectedFuture;
        int _reordered;
        int _evicted;

        public int Count { get { return _count; } }
        public int Inserted { get { return _inserted; } }
        public int RejectedDuplicate { get { return _rejectedDuplicate; } }
        public int RejectedAncient { get { return _rejectedAncient; } }
        public int RejectedFuture { get { return _rejectedFuture; } }
        public int Reordered { get { return _reordered; } }
        public int Evicted { get { return _evicted; } }

        public double OldestMs { get { return _count > 0 ? _ms[_order[0]] : 0.0; } }
        public double NewestMs { get { return _count > 0 ? _ms[_order[_count - 1]] : 0.0; } }
        public double SpanMs { get { return _count > 1 ? NewestMs - OldestMs : 0.0; } }

        public void Clear()
        {
            _count = 0;
            for (int i = 0; i < Capacity; i++) _used[i] = false;
        }

        public void ResetCounters()
        {
            _inserted = 0;
            _rejectedDuplicate = 0;
            _rejectedAncient = 0;
            _rejectedFuture = 0;
            _reordered = 0;
            _evicted = 0;
        }

        public bool Insert(uint seq, double remoteMs, float[] pose, int poseOffset)
        {
            if (pose == null) return false;
            if (poseOffset < 0 || poseOffset + VpbPose.PoseFloats > pose.Length) return false;
            if (double.IsNaN(remoteMs) || double.IsInfinity(remoteMs)) return false;

            for (int i = 0; i < _count; i++)
            {
                if (_seq[_order[i]] == seq)
                {
                    _rejectedDuplicate++;
                    return false;
                }
            }

            if (_count > 0 && remoteMs > NewestMs + FutureLimitMs)
            {
                _rejectedFuture++;
                return false;
            }

            if (_count == Capacity && seq < _seq[_order[0]])
            {
                _rejectedAncient++;
                return false;
            }

            int slot;
            if (_count == Capacity)
            {
                slot = _order[0];
                for (int i = 1; i < _count; i++) _order[i - 1] = _order[i];
                _count--;
                _evicted++;
            }
            else
            {
                slot = -1;
                for (int i = 0; i < Capacity; i++)
                {
                    if (_used[i]) continue;
                    slot = i;
                    break;
                }
                if (slot < 0) return false;
            }

            _used[slot] = true;
            _seq[slot] = seq;
            _ms[slot] = remoteMs;
            Buffer.BlockCopy(pose, poseOffset * 4, _poses, slot * VpbPose.PoseFloats * 4,
                VpbPose.PoseFloats * 4);

            int at = _count;
            while (at > 0 && _seq[_order[at - 1]] > seq)
            {
                _order[at] = _order[at - 1];
                at--;
            }
            if (at != _count) _reordered++;
            _order[at] = slot;
            _count++;
            _inserted++;
            return true;
        }

        public VpbNetSampleState Sample(double renderRemoteMs, float[] outPose)
        {
            if (outPose == null || outPose.Length < VpbPose.PoseFloats) return VpbNetSampleState.Empty;
            if (_count == 0) return VpbNetSampleState.Empty;

            if (_count == 1)
            {
                CopyOut(_order[0], outPose);
                return renderRemoteMs > _ms[_order[0]] ? VpbNetSampleState.Frozen : VpbNetSampleState.Interpolated;
            }

            int oldest = _order[0];
            if (renderRemoteMs <= _ms[oldest])
            {
                CopyOut(oldest, outPose);
                return VpbNetSampleState.Interpolated;
            }

            int newest = _order[_count - 1];
            if (renderRemoteMs <= _ms[newest])
            {
                for (int i = _count - 1; i > 0; i--)
                {
                    int hi = _order[i];
                    int lo = _order[i - 1];
                    if (_ms[lo] > renderRemoteMs) continue;
                    double span = _ms[hi] - _ms[lo];
                    float u = span > 0.000001 ? (float)((renderRemoteMs - _ms[lo]) / span) : 0f;
                    Blend(lo, hi, u, outPose);
                    return VpbNetSampleState.Interpolated;
                }
                CopyOut(oldest, outPose);
                return VpbNetSampleState.Interpolated;
            }

            double ahead = renderRemoteMs - _ms[newest];

            int prev = _order[_count - 2];
            double dt = _ms[newest] - _ms[prev];
            if (dt <= 0.000001)
            {
                CopyOut(newest, outPose);
                return VpbNetSampleState.Frozen;
            }

            double eff = ahead > MaxExtrapolateMs ? MaxExtrapolateMs : ahead;
            double disp = eff - eff * eff * eff / (3.0 * MaxExtrapolateMs * MaxExtrapolateMs);
            float u2 = (float)(1.0 + disp / dt);
            Blend(prev, newest, u2, outPose);
            return ahead > MaxExtrapolateMs ? VpbNetSampleState.Frozen : VpbNetSampleState.Extrapolated;
        }

        void CopyOut(int slot, float[] outPose)
        {
            Buffer.BlockCopy(_poses, slot * VpbPose.PoseFloats * 4, outPose, 0, VpbPose.PoseFloats * 4);
        }

        void Blend(int lo, int hi, float u, float[] outPose)
        {
            int a = lo * VpbPose.PoseFloats;
            int b = hi * VpbPose.PoseFloats;

            for (int i = 0; i < VpbPose.ControllerCount; i++)
            {
                int p = i * VpbPose.FloatsPerController;
                int ia = a + p;
                int ib = b + p;

                outPose[p] = _poses[ia] + (_poses[ib] - _poses[ia]) * u;
                outPose[p + 1] = _poses[ia + 1] + (_poses[ib + 1] - _poses[ia + 1]) * u;
                outPose[p + 2] = _poses[ia + 2] + (_poses[ib + 2] - _poses[ia + 2]) * u;

                Slerp(_poses[ia + 3], _poses[ia + 4], _poses[ia + 5], _poses[ia + 6],
                    _poses[ib + 3], _poses[ib + 4], _poses[ib + 5], _poses[ib + 6],
                    u, outPose, p + 3);
            }
        }

        public static void Slerp(float ax, float ay, float az, float aw,
            float bx, float by, float bz, float bw,
            float t, float[] dst, int at)
        {
            float dot = ax * bx + ay * by + az * bz + aw * bw;
            if (dot < 0f)
            {
                bx = -bx;
                by = -by;
                bz = -bz;
                bw = -bw;
                dot = -dot;
            }
            if (dot > 1f) dot = 1f;

            float wa, wb;
            if (dot > 0.9995f)
            {
                wa = 1f - t;
                wb = t;
            }
            else
            {
                double theta = Math.Acos(dot);
                double sin = Math.Sin(theta);
                wa = (float)(Math.Sin((1.0 - t) * theta) / sin);
                wb = (float)(Math.Sin(t * theta) / sin);
            }

            float x = ax * wa + bx * wb;
            float y = ay * wa + by * wb;
            float z = az * wa + bz * wb;
            float w = aw * wa + bw * wb;

            float mag2 = x * x + y * y + z * z + w * w;
            if (mag2 < 1e-12f)
            {
                dst[at] = 0f;
                dst[at + 1] = 0f;
                dst[at + 2] = 0f;
                dst[at + 3] = 1f;
                return;
            }
            float inv = (float)(1.0 / Math.Sqrt(mag2));
            dst[at] = x * inv;
            dst[at + 1] = y * inv;
            dst[at + 2] = z * inv;
            dst[at + 3] = w * inv;
        }
    }
}
