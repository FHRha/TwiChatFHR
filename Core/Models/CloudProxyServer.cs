using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TwitchChatCore.Core.Models;

public class CloudProxyServer : INotifyPropertyChanged
{
    private string _statusText = "Не проверен";
    private string _statusColor = "#94A3B8";

    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Новый Прокси";
    public string Url { get; set; } = "";
    public string Token { get; set; } = "";
    
    public string StatusText 
    { 
        get => _statusText; 
        set 
        { 
            _statusText = value; 
            OnPropertyChanged(); 
        } 
    }

    public string StatusColor 
    { 
        get => _statusColor; 
        set 
        { 
            _statusColor = value; 
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
