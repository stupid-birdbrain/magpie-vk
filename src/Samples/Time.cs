using System.Diagnostics;

namespace Samples;

public class Time {
    private static Stopwatch _stopwatch = new Stopwatch();
    private static float _deltaTime;
    private static float _globalTime;

    public Time() {
        _stopwatch = new Stopwatch();
    }

    public static void Start() {
        _stopwatch.Start();
    }

    public static void Stop() {
        _stopwatch.Stop();
    }

    public static void Reset() {
        _stopwatch.Reset();
        _globalTime = 0;
        _deltaTime = 0;
    }

    public static void Update() {
        long elapsedTicks = _stopwatch.ElapsedTicks;
        _deltaTime = (float)(_stopwatch.ElapsedTicks / (double)Stopwatch.Frequency);
        _globalTime += _deltaTime;

        _stopwatch.Restart();
    }

    public static float DeltaTime => _deltaTime;
    public static float GlobalTime => _globalTime;
}