using BeatSaber.BeatAvatarSDK;
using UnityEngine;

namespace MultiplayerChat.AvatarColoring;

// Tracks the last color edit request so RGBA sliders can match AvatarData (fixes alpha stuck at 1 on open).
internal static class AvatarColorEditContext
{
    private static Color? _pendingInitialColor;

    internal static AvatarPart LastPart { get; private set; }

    internal static int LastUvSegment { get; private set; }

    internal static void OnColorEditRequested(Color currentColor, AvatarPart part, int uvSegment)
    {
        _pendingInitialColor = currentColor;
        LastPart = part;
        LastUvSegment = uvSegment;
    }

    internal static bool TryConsumePendingInitialColor(out Color c)
    {
        if (_pendingInitialColor.HasValue)
        {
            c = _pendingInitialColor.Value;
            _pendingInitialColor = null;
            return true;
        }

        c = default;
        return false;
    }
}
