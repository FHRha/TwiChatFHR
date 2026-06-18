using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TwitchChatCore.Core.Models;

public class CloudProxyServer : INotifyPropertyChanged
{
    private int _usageSeconds;

    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Новый Прокси";
    public string Url { get; set; } = "";
    public string Token { get; set; } = "";
    
    public int UsageSeconds 
    { 
        get => _usageSeconds; 
        set 
        { 
            _usageSeconds = value; 
            OnPropertyChanged(); 
        } 
    }
    
    public bool IsEnabled { get; set; } = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
