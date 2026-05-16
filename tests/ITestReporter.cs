#if DEBUG
#nullable enable

using System;
using Microsoft.Xna.Framework;
using Terraria.ModLoader;

namespace TheArchitectsBlueprintMod.Tests
{
    /// <summary>Receives pass/fail output from in-game test scripts.</summary>
    internal interface ITestReporter
    {
        void Info(string message);
        void Pass(string step);
        void Fail(string step, string? detail = null);
    }

    internal sealed class CommandTestReporter(CommandCaller caller) : ITestReporter
    {
        public void Info(string message) => caller.Reply(message, Color.White);

        public void Pass(string step) => caller.Reply($"Step {step}: PASS", Color.LimeGreen);

        public void Fail(string step, string? detail = null)
        {
            string msg = string.IsNullOrEmpty(detail) ? $"Step {step}: FAIL" : $"Step {step}: FAIL ({detail})";
            caller.Reply(msg, Color.OrangeRed);
        }
    }

    internal sealed class ChatTestReporter(Action<string, Color> emit) : ITestReporter
    {
        public void Info(string message) => emit(message, Color.White);

        public void Pass(string step) => emit($"[tab.test] {step}: PASS", Color.LimeGreen);

        public void Fail(string step, string? detail = null)
        {
            string msg = string.IsNullOrEmpty(detail) ? $"[tab.test] {step}: FAIL" : $"[tab.test] {step}: FAIL ({detail})";
            emit(msg, Color.OrangeRed);
        }
    }
}
#endif
