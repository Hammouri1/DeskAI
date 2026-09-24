namespace DeskAI.IconProbe;

// Shared with the folder-color probe (ADR 0046), which links this file.
internal readonly record struct Point(int X, int Y);

internal readonly record struct Rect(int Left, int Top, int Right, int Bottom);
