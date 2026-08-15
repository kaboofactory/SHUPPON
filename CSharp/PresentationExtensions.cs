namespace StarRunnerPrototype;

internal static class PresentationExtensions
{
    public static string JapaneseName(this PlayerId player) =>
        player == PlayerId.Player1 ? "青 (P1)" : "赤 (P2)";
}
