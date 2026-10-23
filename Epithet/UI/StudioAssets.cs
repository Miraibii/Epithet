using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Epithet.UI;

/// <summary>Embedded user-provided gold icons. Shared wraps are frame-scoped, never cached.</summary>
internal static class StudioAssets
{
    public static bool Draw(string key, Vector2 pos, Vector2 size, float alpha = 1f)
    {
        try
        {
            var shared = Plugin.Textures.GetFromManifestResource(typeof(Plugin).Assembly, $"Epithet.Assets.{key}.png");
            var wrap = shared.GetWrapOrDefault();
            if (wrap is null) return false; // asset may still be loading on first frame
            ImGui.GetWindowDrawList().AddImage(wrap.Handle, pos, pos + size,
                Vector2.Zero, Vector2.One,
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, Math.Clamp(alpha, 0f, 1f))));
            return true;
        }
        catch (Exception ex)
        {
            // A broken texture must never corrupt the shared ImGui state.
            Plugin.Log.Warning(ex, "Epithet UI image could not render: " + key);
            return false;
        }
    }

    public static string Page(int index) => index switch
    {
        0 => "Overview", 1 => "TitleLibrary", 2 => "ColorsMotion", 3 => "Characters",
        4 => "Compatibility", _ => "Backups"
    };
}
