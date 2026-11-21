using Teg.Application.Ports;

namespace Teg.Application.Tests.Support;

/// <summary>A seed source that hands out the given seeds in order (and starts over), counting how often it was asked.</summary>
internal sealed class FixedSeedSource(params long[] seeds) : ISeedSource
{
    public int Calls { get; private set; }

    public long NextSeed() => seeds[Calls++ % seeds.Length];
}
