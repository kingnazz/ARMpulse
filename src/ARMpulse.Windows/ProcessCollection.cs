using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using ARMpulse.Core;

namespace ARMpulse.Windows;

internal sealed class ProcessCollection : ObservableCollection<ProcessSample>
{
    public void ReplaceAll(IEnumerable<ProcessSample> processes)
    {
        var snapshot = processes.ToArray();
        CheckReentrancy();
        Items.Clear();
        foreach (var item in snapshot) Items.Add(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
