﻿using LiveSplit.Model;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;
using LiveSplit.UI.Util;

namespace LiveSplit.UI.Components
{
    public class QuestTrackerComponent : IComponent
    {
        protected InfoTextComponent InternalComponent { get; set; }

        public QuestTrackerSettings Settings { get; set; }

        protected LiveSplitState CurrentState { get; set; }

        private MemoryReader MemoryReader { get; set; }

        private Color statusColor = Color.White;

        private string missedQuestsAddress = "0x35C094";

        private string oolStateAddress = "0x362B58";

        private string currentLevelAddress = "0x362B5C";

        private bool closed;

        private bool runComplete;

        private RunState runState = RunState.GAMENOTSTARTED;

        private int missedQuestsCount;

        private RunDetails runDetails;

        public string ComponentName => "The Hobbit - All Quests Tracker";

        public float HorizontalWidth => InternalComponent.HorizontalWidth;
        public float MinimumWidth => InternalComponent.MinimumWidth;
        public float VerticalHeight => InternalComponent.VerticalHeight;
        public float MinimumHeight => InternalComponent.MinimumHeight;

        public float PaddingTop => InternalComponent.PaddingTop;
        public float PaddingLeft => InternalComponent.PaddingLeft;
        public float PaddingBottom => InternalComponent.PaddingBottom;
        public float PaddingRight => InternalComponent.PaddingRight;

        public IDictionary<string, Action> ContextMenuControls => null;

        public QuestTrackerComponent(LiveSplitState state)
        {
            Settings = new QuestTrackerSettings();
            InternalComponent = new InfoTextComponent("", null);

            MemoryReader = new MemoryReader();
            runDetails = new RunDetails("placeholder");

            CurrentState = state;

            state.OnStart += state_OnStart;
            state.OnReset += state_OnReset;
            state.OnSplit += state_OnSplit;
        }

        public void DrawHorizontal(Graphics g, LiveSplitState state, float height, Region clipRegion)
        {
            InternalComponent.NameLabel.HasShadow
                = InternalComponent.ValueLabel.HasShadow
                = state.LayoutSettings.DropShadows;

            InternalComponent.NameLabel.ForeColor = state.LayoutSettings.TextColor;
            InternalComponent.ValueLabel.ForeColor = statusColor;

            InternalComponent.DrawHorizontal(g, state, height, clipRegion);
        }

        public void DrawVertical(Graphics g, LiveSplitState state, float width, Region clipRegion)
        {
            InternalComponent.DisplayTwoRows = !Settings.LiteMode;

            InternalComponent.NameLabel.HasShadow
                = InternalComponent.ValueLabel.HasShadow
                = state.LayoutSettings.DropShadows;

            InternalComponent.NameLabel.HorizontalAlignment = Settings.LiteMode ? StringAlignment.Near : StringAlignment.Center;
            InternalComponent.ValueLabel.HorizontalAlignment = Settings.LiteMode ? StringAlignment.Far : StringAlignment.Center;
            InternalComponent.NameLabel.VerticalAlignment = StringAlignment.Center;
            InternalComponent.ValueLabel.VerticalAlignment = StringAlignment.Center;

            InternalComponent.NameLabel.ForeColor = state.LayoutSettings.TextColor;
            InternalComponent.ValueLabel.ForeColor = statusColor;

            InternalComponent.NameLabel.Text = Settings.LiteMode ? "Hobbit AQ Tracker" : "The Hobbit - All Quests Tracker";

            InternalComponent.DrawVertical(g, state, width, clipRegion);
        }

        public Control GetSettingsControl(LayoutMode mode)
        {
            Settings.Mode = mode;
            return Settings;
        }

        public System.Xml.XmlNode GetSettings(System.Xml.XmlDocument document)
        {
            return Settings.GetSettings(document);
        }

        public void SetSettings(System.Xml.XmlNode settings)
        {
            Settings.SetSettings(settings);
        }

        public void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode)
        {
            Process[] processes = Process.GetProcessesByName("meridian");
            if (closed && processes.Length > 0)
            {
                runComplete = false;
                closed = false;
                statusColor = Color.White;
                runState = RunState.WAITING;
            }
            else
            {
                if (runState == RunState.GAMENOTSTARTED) runState = RunState.WAITING;
                if (processes.Length == 0)
                {
                    closed = true;
                    if (state.CurrentPhase == TimerPhase.Running)
                    {
                        runState = RunState.CRASHED;
                        if(Settings.AutoReset) CurrentAutosplitter.DeactivateReset();
                    }
                    else
                    {
                        statusColor = Color.White;
                        runState = RunState.GAMENOTSTARTED;
                    }
                }

                if (runComplete)
                {
                    EndOfRunQuestCheck();
                }

                InternalComponent.InformationValue = SetInformationText();
                InternalComponent.Update(invalidator, state, width, height, mode);
            }
        }

        private void EndOfRunQuestCheck()
        {
            byte[] stateMem = MemoryReader.ReadMemory("meridian", MemoryReader.ConstructPointer(oolStateAddress), true);
            if (stateMem != null)
            {
                int oolState = MemReaderUtil.ConvertMemory(stateMem, MemType.INT);
                if (oolState == 12)
                {
                    runComplete = false;
                    byte[] questMem = MemoryReader.ReadMemory("meridian", MemoryReader.ConstructPointer(missedQuestsAddress), true);
                    if (questMem != null)
                    {
                        missedQuestsCount = MemReaderUtil.ConvertMemory(questMem, MemType.FLOAT);
                        bool missedQuests = missedQuestsCount > 0 ? true : false;
                        if (runDetails.questMissed == Level.None && missedQuests) runDetails.questMissed = Level.CloudsBurst;

                        if (!WebClient.SendRunToServer(runDetails))
                        {
                            runState = RunState.CANTVERIFY;
                            statusColor = Color.Red;
                        }
                        else
                        {
                            runState = RunState.COUNTED;
                            if (missedQuests) statusColor = Color.Red;
                            else statusColor = Color.LimeGreen;
                        }
                    }
                }
            }
        }

        private void EndOfLevelQuestCheck()
        {
            if (runDetails.questMissed == Level.None)
            {
                byte[] questMem = MemoryReader.ReadMemory("meridian", MemoryReader.ConstructPointer(missedQuestsAddress), true);
                if (questMem != null)
                {
                    missedQuestsCount = MemReaderUtil.ConvertMemory(questMem, MemType.FLOAT);
                    bool missedQuests = missedQuestsCount > 0 ? true : false;
                    if (missedQuests)
                    {
                        byte[] levelMem = MemoryReader.ReadMemory("meridian", MemoryReader.ConstructPointer(currentLevelAddress), true);
                        int levelID = MemReaderUtil.ConvertMemory(levelMem, MemType.INT);
                        runDetails.questMissed = (Level)(levelID - 1);
                    }
                }
            }
        }

        private void state_OnStart(object sender, EventArgs e)
        {
            statusColor = Color.Gold;
            runState = RunState.RUNNING;
            if (CurrentAutosplitter.component == null)
            {
                foreach (IComponent c in CurrentState.Layout.Components)
                {
                    if (c.ComponentName == "Scriptable Auto Splitter")
                    {
                        CurrentAutosplitter.component = c;
                        if(CurrentState.Run.IsAutoSplitterActive()) CurrentState.Run.AutoSplitter.Deactivate();
                        return;
                    }
                }

                if (CurrentState.Run.IsAutoSplitterActive())
                {
                    CurrentAutosplitter.component = CurrentState.Run.AutoSplitter.Component;
                }
            }
        }

        private void state_OnReset(object sender, TimerPhase e)
        {
            missedQuestsCount = 0;
            statusColor = Color.White;
            runState = RunState.WAITING;
            runDetails.Clear();
        }

        private void state_OnSplit(Object sender, EventArgs e)
        {
            if (CurrentState.CurrentPhase == TimerPhase.Ended && CurrentState.CurrentSplitIndex >= CurrentState.Run.Count && !runComplete)
            {
                runDetails.completionDate = DateTime.Now;
                runDetails.runTime = CurrentState.CurrentTime;
                statusColor = Color.CadetBlue;
                runState = RunState.FINISHED;
                runComplete = true;
            }
            else EndOfLevelQuestCheck();
        }

        private string SetInformationText()
        {
            switch (runState)
            {
                case RunState.GAMENOTSTARTED:
                    return "-";
                case RunState.WAITING:
                    if (Settings.LiteMode) return "Waiting...";
                    else return "Waiting for run to start...";
                case RunState.CRASHED:
                    if (Settings.LiteMode) return "In progress...";
                    else return "Game crash detected, run still in progress...";
                case RunState.RUNNING:
                    if (Settings.LiteMode) return "In progress...";
                    else return "Run currently in progress...";
                case RunState.FINISHED:
                    if (Settings.LiteMode) return "Complete! Counting Quests...";
                    else return "Run Complete! Skip end cinema for final count.";
                case RunState.CANTVERIFY:
                    if (Settings.LiteMode) return "Can't verify run!";
                    else return "Can't verify run! Server unresponsive.";
                case RunState.COUNTED:
                    if(missedQuestsCount > 0)
                    {
                        if (Settings.LiteMode) return $"Missed {missedQuestsCount} quests!";
                        else return $"Run Invalid, missed {missedQuestsCount} quests!";
                    }
                    else
                    {
                        if (Settings.LiteMode) return "All Quests Finished!";
                        else return "Congratulations, All Quests have been successfully finished!";
                    }
                default:
                    return "-";
            }
        }

        public void Dispose()
        {
            CurrentState.OnStart -= state_OnStart;
            CurrentState.OnReset -= state_OnReset;
            CurrentState.OnSplit -= state_OnSplit;
        }

        public int GetSettingsHashCode() => Settings.GetSettingsHashCode();
    }
}