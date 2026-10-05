using System;
using NAudio.Dsp;
using NAudio.Wave;

namespace WinNotch.Services
{
    /// <summary>
    /// Real audio visualizer: listens to what the PC is playing (loopback of the default output, nothing is recorded
    /// or stored) and turns it into frequency bands. Runs only while the Home panel is open.
    /// </summary>
    public sealed class Visualizer : IDisposable
    {
        public const int BandCount = 44;
        private const int FftSize = 1024;           // 2^10
        private const int FftPow = 10;

        private WasapiLoopbackCapture _cap;
        private readonly float[] _ring = new float[FftSize];
        private int _ringPos;
        private readonly Complex[] _fft = new Complex[FftSize];
        private readonly float[] _bands = new float[BandCount];
        private readonly object _lock = new object();
        private int _channels = 2;

        public bool Running => _cap != null;

        public void Start()
        {
            if (_cap != null) return;
            try
            {
                _cap = new WasapiLoopbackCapture();
                _channels = _cap.WaveFormat.Channels;
                _cap.DataAvailable += OnData;
                _cap.RecordingStopped += (s, e) => { };
                _cap.StartRecording();
            }
            catch (Exception ex)
            {
                App.Log("Vizualizator: " + ex.Message);
                _cap = null;
            }
        }

        public void Stop()
        {
            var c = _cap;
            _cap = null;
            if (c == null) return;
            try { c.DataAvailable -= OnData; c.StopRecording(); c.Dispose(); } catch { }
            lock (_lock) Array.Clear(_bands, 0, _bands.Length);
        }

        private void OnData(object sender, WaveInEventArgs e)
        {
            // Loopback delivers 32-bit float samples, interleaved by channel.
            int frames = e.BytesRecorded / 4 / _channels;
            for (int f = 0; f < frames; f++)
            {
                float sum = 0;
                for (int ch = 0; ch < _channels; ch++) sum += BitConverter.ToSingle(e.Buffer, (f * _channels + ch) * 4);
                _ring[_ringPos] = sum / _channels;
                _ringPos = (_ringPos + 1) % FftSize;
                if (_ringPos == 0) Analyze();
            }
        }

        private void Analyze()
        {
            for (int i = 0; i < FftSize; i++)
            {
                _fft[i].X = (float)(_ring[i] * FastFourierTransform.HannWindow(i, FftSize));
                _fft[i].Y = 0;
            }
            FastFourierTransform.FFT(true, FftPow, _fft);

            // Log-spaced bands from ~60 Hz to ~16 kHz (bins of ~47 Hz at 48 kHz).
            int half = FftSize / 2;
            lock (_lock)
            {
                for (int b = 0; b < BandCount; b++)
                {
                    int lo = (int)(1.3 * Math.Pow(half / 1.3, (double)b / BandCount));
                    int hi = Math.Max(lo + 1, (int)(1.3 * Math.Pow(half / 1.3, (double)(b + 1) / BandCount)));
                    double mag = 0;
                    for (int k = lo; k < hi && k < half; k++)
                        mag = Math.Max(mag, Math.Sqrt(_fft[k].X * _fft[k].X + _fft[k].Y * _fft[k].Y));
                    double db = 20 * Math.Log10(mag + 1e-9);             // about -90..0
                    float v = (float)Math.Clamp((db + 70) / 60, 0, 1);
                    _bands[b] = Math.Max(v, _bands[b] * 0.82f);          // rise fast, fall smoothly
                }
            }
        }

        /// <summary>Copy of the current band levels, 0..1.</summary>
        public float[] Bands()
        {
            lock (_lock)
            {
                var copy = new float[BandCount];
                Array.Copy(_bands, copy, BandCount);
                for (int i = 0; i < BandCount; i++) _bands[i] *= 0.96f;   // decay when no new audio arrives
                return copy;
            }
        }

        public void Dispose() => Stop();
    }
}
