using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Epithet.Models;
using Epithet.UI;
using Newtonsoft.Json;

namespace Epithet.Windows;

public sealed partial class DashboardWindow
{
    private void DrawBackups()
    {
        Hero("LOCAL BACKUPS", "Your titles belong to you.",
            "Export, import and restore profiles without online accounts.","▣",136f);
        ImGui.Spacing();
        Card("##backup_panel", 560, () =>
        {
            Caption("LOCAL BACKUPS", "Export/import profiles without a subscription, account or hosting.");
            ImGui.TextColored(Mint, "COMPLETE BACKUP");
            ImGui.TextWrapped("Export all your character profiles, titles, and named palettes as one JSON file (clipboard). Save the text locally as a backup.");
            if (StyledButton("Copy COMPLETE backup JSON"))
            {
                var bundle = new BackupBundle { Profiles = cfg.Profiles.ToList(), Palettes = cfg.Palettes.ToList() };
                ImGui.SetClipboardText(JsonConvert.SerializeObject(bundle, Formatting.Indented));
                notice = "Complete backup copied to clipboard. Save it as a .json file.";
            }
            if (StyledButton("Restore COMPLETE backup from clipboard"))
            {
                try
                {
                    var json = ImGui.GetClipboardText();
                    if (json.Length > 2_000_000) throw new FormatException("Backup too large");
                    var bundle = JsonConvert.DeserializeObject<BackupBundle>(json)
                        ?? throw new FormatException("Empty backup");
                    if (bundle.FormatVersion != 1 || bundle.Profiles is null || bundle.Palettes is null ||
                        bundle.Profiles.Count > 100 || bundle.Palettes.Count > 250)
                        throw new FormatException("Unsupported or oversized backup");
                    foreach (var p in bundle.Profiles)
                    {
                        if (p is null || string.IsNullOrWhiteSpace(p.CharacterName) ||
                            p.CharacterName.Length > 65 || p.Titles is null || p.Titles.Count > 100)
                            throw new FormatException("Invalid character profile");
                        foreach (var t in p.Titles)
                        {
                            if (t is null || string.IsNullOrWhiteSpace(t.Text) || t.Text.Length > 32 ||
                                t.Text.Any(char.IsControl) ||
                                !ColorUtils.TryParseHex(t.StartColor, out _) ||
                                !ColorUtils.TryParseHex(t.MiddleColor, out _) ||
                                !ColorUtils.TryParseHex(t.EndColor, out _))
                                throw new FormatException("Invalid title data");
                        }
                    }
                    foreach (var color in bundle.Palettes)
                    {
                        if (color is null || string.IsNullOrWhiteSpace(color.Name) || color.Name.Length > 42 ||
                            !ColorUtils.TryParseHex(color.StartColor, out _) ||
                            !ColorUtils.TryParseHex(color.MiddleColor, out _) ||
                            !ColorUtils.TryParseHex(color.EndColor, out _))
                            throw new FormatException("Invalid saved palette");
                    }
                    pendingFullRestore = bundle;
                    notice = "Backup validated. Confirm to REPLACE your local profiles and palettes.";
                }
                catch(Exception ex) { pendingFullRestore = null; notice = "Invalid backup: " + ex.Message; }
            }
            if (pendingFullRestore is not null)
            {
                ImGui.TextColored(Warning, "This will REPLACE all existing profiles and palettes!");
                if (StyledButton("Confirm full restore"))
                {
                    cfg.Profiles = pendingFullRestore.Profiles;
                    cfg.Palettes = pendingFullRestore.Palettes;
                    cfg.SelectedProfileId = cfg.Profiles.FirstOrDefault()?.Id ?? "";
                    cfg.Normalize();
                    pendingFullRestore = null;
                    MarkDirty();
                    notice = "Full backup restored. Auto-save scheduled.";
                }
                ImGui.SameLine();
                if (StyledButton("Cancel restore")) pendingFullRestore = null;
            }
            ImGui.Spacing(); ImGui.Separator();
            ImGui.TextColored(Mint, "INDIVIDUAL PROFILE");

            if (StyledButton("Copy profile JSON"))
            {
                var selected=cfg.Profiles.FirstOrDefault(x=>x.Id==cfg.SelectedProfileId)
                             ??cfg.Profiles.FirstOrDefault(IsRealProfile);
                if(selected is null) notice="Create or bind a profile before exporting.";
                else
                {
                    ImGui.SetClipboardText(JsonConvert.SerializeObject(selected, Formatting.Indented));
                    notice="Profile copied to clipboard.";
                }
            }
            if (StyledButton("Import profile JSON"))
            {
                try
                {
                    var json = ImGui.GetClipboardText();
                    if (json.Length > 300_000) throw new FormatException("Import too large");
                    var imported = JsonConvert.DeserializeObject<CharacterProfile>(json) ?? throw new FormatException("Empty profile");
                    if (string.IsNullOrWhiteSpace(imported.CharacterName) || imported.Titles == null || imported.Titles.Count > 100)
                        throw new FormatException("Invalid profile");
                    imported.Id = Guid.NewGuid().ToString("N");
                    foreach (var t in imported.Titles)
                    {
                        if (t == null || t.Text == null || t.Text.Length > 32 || t.Text.Any(char.IsControl))
                            throw new FormatException("Invalid title text");
                        t.Id = Guid.NewGuid().ToString("N");
                        t.ColorCount = Math.Clamp(t.ColorCount, 1, 3);
                        if (!ColorUtils.TryParseHex(t.StartColor, out _) ||
                            !ColorUtils.TryParseHex(t.MiddleColor, out _) ||
                            !ColorUtils.TryParseHex(t.EndColor, out _)) throw new FormatException("Invalid title colors");
                    }
                    imported.ActiveTitleId = imported.Titles.FirstOrDefault()?.Id ?? "";
                    imported.AnimationSpeed = Math.Clamp(float.IsFinite(imported.AnimationSpeed) ? imported.AnimationSpeed : 1.5f, .25f, 6f);
                    cfg.Profiles.Add(imported); cfg.SelectedProfileId = imported.Id; MarkDirty();
                    notice = "Profile imported successfully.";
                }
                catch (Exception ex) { notice = "Import failed: " + ex.Message; }
            }
            ImGui.Separator();
            if (StyledButton("Save now")) SaveNow(true);
            ImGui.TextColored(Muted, "Updates autosave after a short pause. The real character name stays unchanged.");
        });
    }
}
