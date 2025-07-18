﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public interface ICustomActionControlParameter
    {
        string GetValue();
    }

    public class CustomActionControlTextBox : ICustomActionControlParameter
    {
        public string Label { get; set; } = string.Empty;
        public string Placeholder { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;

        public CustomActionControlTextBox(string label, string placeholder, string defaultValue)
        {
            Label = label + ":";
            Placeholder = placeholder;
            Text = defaultValue;
        }

        public string GetValue() => Text;
    }

    public class CustomActionControlPathSelector : ObservableObject, ICustomActionControlParameter
    {
        public string Label { get; set; } = string.Empty;
        public string Placeholder { get; set; } = string.Empty;
        public bool IsFolder { get; set; } = false;

        public string Path
        {
            get => _path;
            set => SetProperty(ref _path, value);
        }

        public CustomActionControlPathSelector(string label, string placeholder, bool isFolder, string defaultValue)
        {
            Label = label + ":";
            Placeholder = placeholder;
            IsFolder = isFolder;
            _path = defaultValue;
        }

        public string GetValue() => _path;

        private string _path = string.Empty;
    }

    public class CustomActionControlCheckBox : ICustomActionControlParameter
    {
        public string Label { get; set; } = string.Empty;
        public string ToolTip { get; set; } = string.Empty;
        public string CheckedValue { get; set; } = string.Empty;
        public bool IsChecked { get; set; }

        public CustomActionControlCheckBox(string label, string tooltip, string checkedValue, bool isChecked)
        {
            Label = label;
            ToolTip = string.IsNullOrEmpty(tooltip) ? null : tooltip;
            CheckedValue = checkedValue;
            IsChecked = isChecked;
        }

        public string GetValue() => IsChecked ? CheckedValue : string.Empty;
    }

    public class ExecuteCustomAction : Popup
    {
        public Models.CustomAction CustomAction
        {
            get;
        }

        public object Target
        {
            get;
        }

        public List<ICustomActionControlParameter> ControlParameters
        {
            get;
        } = [];

        public ExecuteCustomAction(Repository repo, Models.CustomAction action)
        {
            _repo = repo;
            CustomAction = action;
            Target = new Models.Null();
            PrepareControlParameters();
        }

        public ExecuteCustomAction(Repository repo, Models.CustomAction action, Models.Branch branch)
        {
            _repo = repo;
            CustomAction = action;
            Target = branch;
            PrepareControlParameters();
        }

        public ExecuteCustomAction(Repository repo, Models.CustomAction action, Models.Commit commit)
        {
            _repo = repo;
            CustomAction = action;
            Target = commit;
            PrepareControlParameters();
        }

        public ExecuteCustomAction(Repository repo, Models.CustomAction action, Models.Tag tag)
        {
            _repo = repo;
            CustomAction = action;
            Target = tag;
            PrepareControlParameters();
        }

        public override Task<bool> Sure()
        {
            _repo.SetWatcherEnabled(false);
            ProgressDescription = "Run custom action ...";

            var cmdline = PrepareStringByTarget(CustomAction.Arguments);
            for (var i = ControlParameters.Count - 1; i >= 0; i--)
            {
                var param = ControlParameters[i];
                cmdline = cmdline.Replace($"${i + 1}", param.GetValue());
            }

            var log = _repo.CreateLog(CustomAction.Name);
            Use(log);

            return Task.Run(() =>
            {
                log.AppendLine($"$ {CustomAction.Executable} {cmdline}\n");

                if (CustomAction.WaitForExit)
                    RunAndWait(cmdline, log);
                else
                    Run(cmdline);

                log.Complete();
                CallUIThread(() => _repo.SetWatcherEnabled(true));
                return true;
            });
        }

        private void PrepareControlParameters()
        {
            foreach (var ctl in CustomAction.Controls)
            {
                switch (ctl.Type)
                {
                    case Models.CustomActionControlType.TextBox:
                        ControlParameters.Add(new CustomActionControlTextBox(ctl.Label, ctl.Description, PrepareStringByTarget(ctl.StringValue)));
                        break;
                    case Models.CustomActionControlType.CheckBox:
                        ControlParameters.Add(new CustomActionControlCheckBox(ctl.Label, ctl.Description, ctl.StringValue, ctl.BoolValue));
                        break;
                    case Models.CustomActionControlType.PathSelector:
                        ControlParameters.Add(new CustomActionControlPathSelector(ctl.Label, ctl.Description, ctl.BoolValue, PrepareStringByTarget(ctl.StringValue)));
                        break;
                }
            }
        }

        private string PrepareStringByTarget(string org)
        {
            org = org.Replace("${REPO}", GetWorkdir());

            if (Target is Models.Branch b)
                return org.Replace("${BRANCH}", b.FriendlyName);
            else if (Target is Models.Commit c)
                return org.Replace("${SHA}", c.SHA);
            else if (Target is Models.Tag t)
                return org.Replace("${TAG}", t.Name);

            return org;
        }

        private string GetWorkdir()
        {
            return OperatingSystem.IsWindows() ? _repo.FullPath.Replace("/", "\\") : _repo.FullPath;
        }

        private void Run(string args)
        {
            var start = new ProcessStartInfo();
            start.FileName = CustomAction.Executable;
            start.Arguments = args;
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.WorkingDirectory = _repo.FullPath;

            try
            {
                Process.Start(start);
            }
            catch (Exception e)
            {
                CallUIThread(() => App.RaiseException(_repo.FullPath, e.Message));
            }
        }

        private void RunAndWait(string args, Models.ICommandLog log)
        {
            var start = new ProcessStartInfo();
            start.FileName = CustomAction.Executable;
            start.Arguments = args;
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            start.StandardOutputEncoding = Encoding.UTF8;
            start.StandardErrorEncoding = Encoding.UTF8;
            start.WorkingDirectory = _repo.FullPath;

            var proc = new Process() { StartInfo = start };
            var builder = new StringBuilder();

            proc.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    log?.AppendLine(e.Data);
            };

            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    log?.AppendLine(e.Data);
                    builder.AppendLine(e.Data);
                }
            };

            try
            {
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                proc.WaitForExit();

                var exitCode = proc.ExitCode;
                if (exitCode != 0)
                {
                    var errMsg = builder.ToString().Trim();
                    if (!string.IsNullOrEmpty(errMsg))
                        CallUIThread(() => App.RaiseException(_repo.FullPath, errMsg));
                }
            }
            catch (Exception e)
            {
                CallUIThread(() => App.RaiseException(_repo.FullPath, e.Message));
            }

            proc.Close();
        }

        private readonly Repository _repo = null;
    }
}
