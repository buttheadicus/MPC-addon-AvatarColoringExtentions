using BeatSaber.BeatAvatarSDK;
using UnityEngine;

namespace MultiplayerChat.AvatarColoring;

internal static class AvatarDataColorResolver
{
    internal static bool TryGetColor(AvatarData data, AvatarPart part, out Color color)
    {
        color = default;
        switch (part)
        {
            case AvatarPart.HeadTopPrimaryColor:
                color = data.headTopPrimaryColor;
                return true;
            case AvatarPart.HeadTopSecondaryColor:
                color = data.headTopSecondaryColor;
                return true;
            case AvatarPart.GlassesColor:
                color = data.glassesColor;
                return true;
            case AvatarPart.FacialHairColor:
                color = data.facialHairColor;
                return true;
            case AvatarPart.HandsColor:
                color = data.handsColor;
                return true;
            case AvatarPart.ClothesModelPrimaryColor:
                color = data.clothesPrimaryColor;
                return true;
            case AvatarPart.ClothesModelSecondaryColor:
                color = data.clothesSecondaryColor;
                return true;
            case AvatarPart.ClothesModelDetailColor:
                color = data.clothesDetailColor;
                return true;
            default:
                return false;
        }
    }

    internal static bool TrySetColor(AvatarData data, AvatarPart part, Color color)
    {
        switch (part)
        {
            case AvatarPart.HeadTopPrimaryColor:
                data.headTopPrimaryColor = color;
                return true;
            case AvatarPart.HeadTopSecondaryColor:
                data.headTopSecondaryColor = color;
                return true;
            case AvatarPart.GlassesColor:
                data.glassesColor = color;
                return true;
            case AvatarPart.FacialHairColor:
                data.facialHairColor = color;
                return true;
            case AvatarPart.HandsColor:
                data.handsColor = color;
                return true;
            case AvatarPart.ClothesModelPrimaryColor:
                data.clothesPrimaryColor = color;
                return true;
            case AvatarPart.ClothesModelSecondaryColor:
                data.clothesSecondaryColor = color;
                return true;
            case AvatarPart.ClothesModelDetailColor:
                data.clothesDetailColor = color;
                return true;
            default:
                return false;
        }
    }
}
