using DeskAI.IconProbe;

namespace DeskAI.IconProbe.Tests;

public sealed class WaitingTests
{
    [Fact]
    public async Task Returns_the_value_as_soon_as_it_appears()
    {
        var calls = 0;
        var result = await Waiting.ForAsync(() => ++calls == 3 ? "ready" : null, TimeSpan.FromSeconds(60), TimeSpan.Zero, TimeProvider.System);

        Assert.Equal("ready", result);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Gives_up_with_null_after_the_timeout()
    {
        var clock = new SteppingClock(TimeSpan.FromSeconds(10));

        var result = await Waiting.ForAsync<string>(() => null, TimeSpan.FromSeconds(60), TimeSpan.Zero, clock);

        Assert.Null(result);
        Assert.InRange(clock.Reads, 6, 9);
    }

    /// <summary>Each read of the time moves it forward, so a wait ends without sleeping.</summary>
    private sealed class SteppingClock(TimeSpan step) : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;

        public int Reads { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            Reads++;
            _now += step;
            return _now;
        }
    }
}
