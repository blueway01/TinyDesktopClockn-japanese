using System;
using System.Diagnostics;
using System.Net.NetworkInformation;

namespace TinyDesktopClock
{
    internal struct TrafficReading
    {
        public string Status;
        public double DownloadMbps;
        public double UploadMbps;
    }

    internal sealed class PeakTrafficRecord
    {
        public double DownloadMbps { get; private set; }
        public double UploadMbps { get; private set; }

        public void Update(TrafficReading reading)
        {
            if (reading.Status != "接続中") return;
            if (IsValid(reading.DownloadMbps))
                DownloadMbps = Math.Max(DownloadMbps, reading.DownloadMbps);
            if (IsValid(reading.UploadMbps))
                UploadMbps = Math.Max(UploadMbps, reading.UploadMbps);
        }

        private static bool IsValid(double value)
        {
            return value >= 0 && !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    internal sealed class WifiTrafficMeter
    {
        private string _adapterId;
        private long _received;
        private long _sent;
        private long _timestamp;

        public TrafficReading Sample()
        {
            try
            {
                NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
                bool foundWifi = false;
                foreach (NetworkInterface adapter in adapters)
                {
                    if (adapter.NetworkInterfaceType != NetworkInterfaceType.Wireless80211) continue;
                    foundWifi = true;
                    if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                    IPv4InterfaceStatistics counters = adapter.GetIPv4Statistics();
                    long now = Stopwatch.GetTimestamp();
                    long received = counters.BytesReceived;
                    long sent = counters.BytesSent;
                    if (_adapterId != adapter.Id || now <= _timestamp || received < _received || sent < _sent)
                    {
                        _adapterId = adapter.Id;
                        _received = received;
                        _sent = sent;
                        _timestamp = now;
                        return new TrafficReading { Status = "測定準備中" };
                    }
                    double elapsed = (now - _timestamp) / (double)Stopwatch.Frequency;
                    double down = (received - _received) * 8.0 / elapsed / 1000000.0;
                    double up = (sent - _sent) * 8.0 / elapsed / 1000000.0;
                    _received = received;
                    _sent = sent;
                    _timestamp = now;
                    return new TrafficReading { Status = "接続中", DownloadMbps = down, UploadMbps = up };
                }
                _adapterId = null;
                return new TrafficReading { Status = foundWifi ? "Wi-Fi 未接続" : "Wi-Fi なし" };
            }
            catch (Exception)
            {
                _adapterId = null;
                return new TrafficReading { Status = "取得エラー" };
            }
        }
    }
}
