using System.ComponentModel;
using System.Runtime.CompilerServices;
using SaltSteamEditor.Core;

namespace SaltSteamEditor;

public sealed class PreviewField(TemplateField definition) : INotifyPropertyChanged
{
    private string _value = definition.Example;
    public string Key => definition.Key;
    public string Name => definition.Name;
    public string Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
