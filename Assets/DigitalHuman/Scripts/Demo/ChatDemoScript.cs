using System;
using System.Collections.Generic;
using System.Globalization;

namespace DigitalHuman
{
    public enum DemoStepKind
    {
        /// <summary>A line typed into the chat box and sent.</summary>
        Say,
        Wait,
        Camera,
        /// <summary>Sets the pause after each finished action, from here on.</summary>
        Hold,
        /// <summary>Sets the typing speed in characters per second, from here on.</summary>
        Typing,
        /// <summary>The next line is sent without waiting for its action to finish.</summary>
        NoWait
    }

    public readonly struct DemoStep
    {
        public readonly DemoStepKind Kind;
        public readonly int Line;
        /// <summary>The chat line, for <see cref="DemoStepKind.Say"/>.</summary>
        public readonly string Text;
        /// <summary>Seconds for Wait and Hold, characters per second for Typing.</summary>
        public readonly float Value;
        public readonly CameraShot Shot;
        public readonly NpcCameraAngle Angle;

        public DemoStep(
            DemoStepKind kind,
            int line,
            string text = null,
            float value = 0f,
            CameraShot shot = CameraShot.Overview,
            NpcCameraAngle angle = NpcCameraAngle.Front)
        {
            Kind = kind;
            Line = line;
            Text = text ?? string.Empty;
            Value = value;
            Shot = shot;
            Angle = angle;
        }

        public override string ToString()
        {
            switch (Kind)
            {
                case DemoStepKind.Say:
                    return $"line {Line}: \"{Text}\"";
                case DemoStepKind.Camera:
                    if (Shot == CameraShot.FollowNpc)
                    {
                        return $"line {Line}: camera npc {Angle.ToString().ToLowerInvariant()}";
                    }

                    return Shot == CameraShot.Overview
                        ? $"line {Line}: camera overview"
                        : $"line {Line}: camera player";
                case DemoStepKind.NoWait:
                    return $"line {Line}: nowait";
                default:
                    return $"line {Line}: {Kind.ToString().ToLowerInvariant()} {Value.ToString(CultureInfo.InvariantCulture)}";
            }
        }
    }

    /// <summary>
    /// Reads the plain-text scripts <see cref="ChatDemoDirector"/> plays. Each row is a chat
    /// line, except rows starting with <c>@</c>, which are stage directions, and rows
    /// starting with <c>#</c>, which are comments.
    /// </summary>
    /// <remarks>
    /// <code>
    /// @camera overview | player | npc [front | side | back | free]
    /// @wait 2.5      pause for that many seconds
    /// @hold 1.2      pause after every finished action from here on
    /// @typing 16     typing speed in characters per second from here on
    /// @nowait        send the next line without waiting for its action to finish
    /// </code>
    /// A malformed direction is reported and skipped rather than aborting the script, so a
    /// typo costs one cue instead of the whole take.
    /// </remarks>
    public static class ChatDemoScript
    {
        public static List<DemoStep> Parse(string source, ICollection<string> errors = null)
        {
            List<DemoStep> steps = new List<DemoStep>();
            if (string.IsNullOrEmpty(source))
            {
                return steps;
            }

            string[] rows = source.Split('\n');
            for (int i = 0; i < rows.Length; i++)
            {
                int line = i + 1;
                string row = rows[i].Trim();
                if (row.Length == 0 || row[0] == '#')
                {
                    continue;
                }

                if (row[0] != '@')
                {
                    steps.Add(new DemoStep(DemoStepKind.Say, line, row));
                    continue;
                }

                if (TryParseDirection(row, line, out DemoStep step, out string error))
                {
                    steps.Add(step);
                }
                else
                {
                    errors?.Add($"line {line}: {error}");
                }
            }

            return steps;
        }

        private static bool TryParseDirection(
            string row,
            int line,
            out DemoStep step,
            out string error)
        {
            string[] parts = row.Substring(1).Split(
                (char[])null, StringSplitOptions.RemoveEmptyEntries);
            string name = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;
            step = default;
            error = null;

            switch (name)
            {
                case "wait":
                    return TryParseNumber(parts, line, DemoStepKind.Wait, false, out step, out error);
                case "hold":
                    return TryParseNumber(parts, line, DemoStepKind.Hold, false, out step, out error);
                case "typing":
                    return TryParseNumber(parts, line, DemoStepKind.Typing, true, out step, out error);
                case "nowait":
                    if (parts.Length != 1)
                    {
                        error = "'@nowait' takes no arguments";
                        return false;
                    }

                    step = new DemoStep(DemoStepKind.NoWait, line);
                    return true;
                case "camera":
                    return TryParseCamera(parts, line, out step, out error);
                default:
                    error = $"unknown direction '@{name}'";
                    return false;
            }
        }

        private static bool TryParseNumber(
            string[] parts,
            int line,
            DemoStepKind kind,
            bool mustBePositive,
            out DemoStep step,
            out string error)
        {
            step = default;
            error = null;
            string name = kind.ToString().ToLowerInvariant();
            if (parts.Length != 2 ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ||
                float.IsNaN(value) || float.IsInfinity(value) ||
                value < 0f || (mustBePositive && value <= 0f))
            {
                error = mustBePositive
                    ? $"'@{name}' needs one positive number"
                    : $"'@{name}' needs one number of seconds";
                return false;
            }

            step = new DemoStep(kind, line, value: value);
            return true;
        }

        private static bool TryParseCamera(
            string[] parts,
            int line,
            out DemoStep step,
            out string error)
        {
            step = default;
            error = null;
            string target = parts.Length > 1 ? parts[1].ToLowerInvariant() : string.Empty;
            switch (target)
            {
                case "overview":
                case "player":
                    if (parts.Length != 2)
                    {
                        error = $"'@camera {target}' takes no angle";
                        return false;
                    }

                    CameraShot shot = target == "overview"
                        ? CameraShot.Overview
                        : CameraShot.FollowPlayer;
                    step = new DemoStep(DemoStepKind.Camera, line, shot: shot);
                    return true;
                case "npc":
                case "eva":
                    if (parts.Length > 3)
                    {
                        error = "'@camera npc' takes at most one angle";
                        return false;
                    }

                    NpcCameraAngle angle = NpcCameraAngle.Front;
                    if (parts.Length == 3 && !TryParseAngle(parts[2], out angle))
                    {
                        error = $"unknown camera angle '{parts[2]}', expected front, side, back or free";
                        return false;
                    }

                    step = new DemoStep(
                        DemoStepKind.Camera, line, shot: CameraShot.FollowNpc, angle: angle);
                    return true;
                default:
                    error = "'@camera' needs overview, player or npc";
                    return false;
            }
        }

        private static bool TryParseAngle(string text, out NpcCameraAngle angle)
        {
            switch (text.ToLowerInvariant())
            {
                case "front":
                    angle = NpcCameraAngle.Front;
                    return true;
                case "side":
                    angle = NpcCameraAngle.Side;
                    return true;
                case "back":
                    angle = NpcCameraAngle.Back;
                    return true;
                case "free":
                    angle = NpcCameraAngle.Free;
                    return true;
                default:
                    angle = NpcCameraAngle.Front;
                    return false;
            }
        }
    }
}
