using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CodexVsix.Services;

internal sealed class CodexWebViewStateStore
{
    private readonly object _syncRoot = new();
    private readonly string _stateFile;
    private JObject? _state;

    public CodexWebViewStateStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexVsix");
        _stateFile = Path.Combine(directory, "official-webview-state.json");
    }

    public JObject GetSnapshot()
    {
        lock (_syncRoot)
        {
            return (JObject)LoadState().DeepClone();
        }
    }

    public JToken? Get(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        lock (_syncRoot)
        {
            return LoadState()[key]?.DeepClone();
        }
    }

    public void Set(string key, JToken? value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        lock (_syncRoot)
        {
            var state = LoadState();
            if (value is null || value.Type == JTokenType.Null || value.Type == JTokenType.Undefined)
            {
                state.Remove(key);
            }
            else
            {
                state[key] = value.DeepClone();
            }

            SaveState(state);
        }
    }

    private JObject LoadState()
    {
        if (_state is not null)
        {
            return _state;
        }

        try
        {
            if (File.Exists(_stateFile))
            {
                _state = JObject.Parse(File.ReadAllText(_stateFile));
                return _state;
            }
        }
        catch
        {
        }

        _state = new JObject();
        return _state;
    }

    private void SaveState(JObject state)
    {
        var directory = Path.GetDirectoryName(_stateFile)!;
        Directory.CreateDirectory(directory);
        var temporary = _stateFile + ".tmp";
        File.WriteAllText(temporary, state.ToString(Formatting.Indented));
        if (File.Exists(_stateFile))
        {
            File.Replace(temporary, _stateFile, null);
        }
        else
        {
            File.Move(temporary, _stateFile);
        }
    }
}
