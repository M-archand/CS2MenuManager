﻿using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using CS2MenuManager.API.Menu;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Tomlyn.Model;
using static CS2MenuManager.API.Class.ConfigManager;

namespace CS2MenuManager.API.Class;

internal static partial class Library
{
    private const float TOLERANCE = 0.0001f;
    [GeneratedRegex("<[^>]+>", RegexOptions.Compiled)] private static partial Regex TagRegex();

    public readonly record struct VectorData(Vector Position, QAngle Angle, float? Size);

    private static CCSPlayerPawn? GetPlayerPawn(this CCSPlayerController player)
    {
        if (player.Pawn.Value is not { } pawn)
            return null;

        if (pawn.LifeState != (byte)LifeState_t.LIFE_DEAD)
            return pawn.As<CCSPlayerPawn>();
        
        if (pawn.ObserverServices?.ObserverTarget.Value?.As<CBasePlayerPawn>() is not { } observer)
            return null;
        
        pawn = observer;

        return pawn.As<CCSPlayerPawn>();
    }

    public static VectorData? FindVectorData(this CCSPlayerController player, float? size = null)
    {
        CCSPlayerPawn? playerPawn = GetPlayerPawn(player);
        if (playerPawn == null)
            return null;

        ResolutionManager.Resolution resolution = ResolutionManager.GetPlayerResolution(player);

        QAngle eyeAngles = playerPawn.EyeAngles;
        Vector forward = new(), right = new(), up = new();
        NativeAPI.AngleVectors(eyeAngles.Handle, forward.Handle, right.Handle, up.Handle);

        if (size.HasValue)
        {
            (float newX, float newY, float newSize) = GetWorldTextPosition(player, resolution.PositionX, resolution.PositionY, size.Value);

            resolution.PositionX = newX;
            resolution.PositionY = newY;
            size = newSize;
        }

        Vector offset = forward * 7 + right * resolution.PositionX + up * resolution.PositionY;
        QAngle angle = new()
        {
            Y = eyeAngles.Y + 270,
            Z = 90 - eyeAngles.X,
            X = 0
        };

        return new VectorData()
        {
            Position = playerPawn.AbsOrigin! + offset + new Vector(0, 0, playerPawn.ViewOffset.Z),
            Angle = angle,
            Size = size
        };
    }

    private static (float x, float y, float size) GetWorldTextPosition(CCSPlayerController controller, float x, float y, float size)
    {
        float fov = controller.DesiredFOV == 0 ? 90 : controller.DesiredFOV;

        if (Math.Abs(fov - 90) < TOLERANCE)
            return (x, y, size);

        float scaleFactor = (float)Math.Tan((fov / 2) * Math.PI / 180) / (float)Math.Tan(45 * Math.PI / 180);

        float newX = x * scaleFactor;
        float newY = y * scaleFactor;
        float newSize = size * scaleFactor;

        return (newX, newY, newSize);
    }

    public static CCSGOViewModel? EnsureCustomView(this CCSPlayerController player)
    {
        CCSPlayerPawn? playerPawn = GetPlayerPawn(player);
        if (playerPawn == null)
            return null;

        if (playerPawn.ViewModelServices == null)
            return null;

        int offset = Schema.GetSchemaOffset("CCSPlayer_ViewModelServices", "m_hViewModel");
        IntPtr viewModelHandleAddress = playerPawn.ViewModelServices.Handle + offset + 4;

        CHandle<CCSGOViewModel> handle = new(viewModelHandleAddress);
        if (handle.IsValid)
            return handle.Value;
        
        CCSGOViewModel viewmodel = Utilities.CreateEntityByName<CCSGOViewModel>("predicted_viewmodel")!;
        viewmodel.DispatchSpawn();
        handle.Raw = viewmodel.EntityHandle.Raw;
        Utilities.SetStateChanged(playerPawn, "CCSPlayerPawnBase", "m_pViewModelServices");

        return handle.Value;
    }

    public static CPointWorldText? CreateWorldText(string text, int size, Color color, string font, bool background, float offset)
    {
        CPointWorldText entity = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext")!;

        if (!entity.IsValid)
            return null;

        entity.MessageText = text;
        entity.Enabled = true;
        entity.FontSize = size;
        entity.Fullbright = true;
        entity.Color = color;
        entity.WorldUnitsPerPx = 0.25f / 1050 * size;
        entity.FontName = font;
        entity.JustifyHorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_LEFT;
        entity.JustifyVertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_CENTER;
        entity.ReorientMode = PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_NONE;
        entity.RenderMode = RenderMode_t.kRenderNormal;

        entity.DrawBackground = background;
        entity.BackgroundBorderHeight = 0.1f;
        entity.BackgroundBorderWidth = 0.1f;
        entity.DepthOffset = offset;

        entity.DispatchSpawn();
        return entity;
    }

    public static void CreateFakeWorldText(this CCSPlayerController player, ScreenMenuInstance instance)
    {
        CCSGOViewModel? viewModel = player.EnsureCustomView();
        if (viewModel == null) { instance.Close(false); return; }

        VectorData? vectorData = player.FindVectorData();
        if (vectorData == null) { instance.Close(false); return; }

        CPointWorldText? entity = CreateWorldText("       ", 35, Color.Orange, "Arial", false, 0f);
        if (entity == null) { instance.Close(false); return; }

        entity.Teleport(vectorData.Value.Position, vectorData.Value.Angle);
        entity.AcceptInput("SetParent", viewModel, null, "!activator");

        entity.Remove();
    }

    public static void SaveSpeed(this CCSPlayerController player, ref float oldModifier)
    {
        if (player.PlayerPawn.Value is { } playerPawn)
            oldModifier = playerPawn.VelocityModifier;
    }

    public static void Freeze(this CCSPlayerController player)
    {
        if (player.PlayerPawn.Value is { } playerPawn)
            playerPawn.VelocityModifier = 0.0f;
    }

    public static void Unfreeze(this CCSPlayerController player, float oldModifier)
    {
        if (player.PlayerPawn.Value is { } playerPawn)
            playerPawn.VelocityModifier = oldModifier;
    }

    public static string Localizer(this CCSPlayerController player, string key, params object?[] args)
    {
        CultureInfo cultureInfo = player.GetLanguage();

        if (Config.Lang.TryGetValue(cultureInfo.Name, out Dictionary<string, string>? lang) && lang.TryGetValue(key, out string? text))
            return string.Format(text.ReplaceColorTags(), args);

        string shortName = cultureInfo.TwoLetterISOLanguageName.ToLower();
        return Config.Lang.TryGetValue(shortName, out lang) && lang.TryGetValue(key, out text)
            ? string.Format(text.ReplaceColorTags(), args)
            : Config.Lang.TryGetValue("en", out lang) && lang.TryGetValue(key, out text) ? string.Format(text.ReplaceColorTags(), args) : key;
    }

    public static string TruncateHtml(this string html, int maxLength)
    {
        if (maxLength <= 0 || string.IsNullOrEmpty(html))
            return html;

        string textOnly = TagRegex().Replace(html, string.Empty);
        if (textOnly.Length <= maxLength)
            return html;

        Stack<string> tagStack = new();
        StringBuilder result = new();
        int visibleLength = 0, i = 0;

        while (i < html.Length && visibleLength < maxLength)
        {
            if (html[i] == '<')
            {
                Match match = TagRegex().Match(html, i);
                if (match.Success && match.Index == i)
                {
                    string tag = match.Value;
                    result.Append(tag);
                    i += tag.Length;

                    if (!tag.StartsWith("</", StringComparison.Ordinal))
                    {
                        string tagName = tag.Split(new[] { ' ', '>', '/' }, StringSplitOptions.RemoveEmptyEntries)[0].TrimStart('<');
                        if (!tag.EndsWith("/>", StringComparison.Ordinal) && !tagName.StartsWith('!'))
                            tagStack.Push(tagName);
                    }
                    else if (tagStack.Count > 0)
                    {
                        tagStack.Pop();
                    }

                    continue;
                }
            }

            result.Append(html[i]);
            visibleLength++;
            i++;
        }

        while (tagStack.Count > 0)
            result.Append($"</{tagStack.Pop()}>");

        return result.ToString();
    }

    public static char GetChatColor(this string colorName)
    {
        return (char)typeof(ChatColors).GetField(colorName)?.GetValue(null)!;
    }

    public static T GetValueOrDefault<T>(this TomlTable table, string key, T defaultValue)
    {
        if (!table.TryGetValue(key, out object value))
            return defaultValue;
        
        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return defaultValue;
        }
    }

    public static Color HexToColor(this string hex)
    {
        hex = hex.TrimStart('#');

        return Color.FromArgb(
            red: Convert.ToByte(hex[..2], 16),
            green: Convert.ToByte(hex.Substring(2, 2), 16),
            blue: Convert.ToByte(hex.Substring(4, 2), 16)
        );
    }
}