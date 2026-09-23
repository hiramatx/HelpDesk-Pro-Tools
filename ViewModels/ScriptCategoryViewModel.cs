using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using HelpDesk_Pro_Tools.Models;

namespace HelpDesk_Pro_Tools.ViewModels;

public class ScriptCategoryViewModel : ViewModelBase
{
    public ScriptCategoryViewModel(string name, IEnumerable<ScriptItemViewModel> scripts)
    {
        Name = name;
        Scripts = new ObservableCollection<ScriptItemViewModel>(scripts);
    }

    public string Name { get; }

    public ObservableCollection<ScriptItemViewModel> Scripts { get; }

    public bool IsEmpty => Scripts.Count == 0;
}

public class ScriptItemViewModel : ViewModelBase
{
    public ScriptItemViewModel(ScriptEntry entry, Func<ScriptItemViewModel, Task> run)
    {
        Name = string.IsNullOrWhiteSpace(entry.Name) ? System.IO.Path.GetFileNameWithoutExtension(entry.Path) : entry.Name;
        Path = entry.Path;
        Arguments = entry.Arguments;
        ToolTip = string.IsNullOrWhiteSpace(entry.Description) ? entry.Path : $"{entry.Description}\n{entry.Path}";
        RunCommand = new AsyncRelayCommand(() => run(this));
    }

    public string Name { get; }
    public string Path { get; }
    public string? Arguments { get; }
    public string ToolTip { get; }
    public bool Exists => File.Exists(Path);

    public IAsyncRelayCommand RunCommand { get; }
}
