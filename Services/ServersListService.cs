using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json;

namespace SurvivalBackend.Services
{
    public class ServersListService
    {
        #region Structs

        public class ServerContainer(string uniqueId, string serverName, string requestId)
        {
            public string UniqueId { get; set; } = uniqueId;
            public string ServerName { get; } = serverName;
            public string RequestId { get; set; } = requestId;
            public bool Ready { get; set; }
        }

        #endregion

        public ServersListService()
        {
            _filePath = Path.Combine(AppContext.BaseDirectory, "servers.json");

            _items = [];
            _items.CollectionChanged += OnCollectionChanged;
        }

        private readonly string _filePath;
        private readonly ObservableCollection<ServerContainer> _items;

        private readonly JsonSerializerOptions _jsonSerializerOptions = new() { WriteIndented = true };

        private bool _isLoaded;

        public ObservableCollection<ServerContainer> Items
        {
            get
            {
                if (!_isLoaded)
                {
                    Load();

                    _isLoaded = true;
                }

                return _items;
            }
        }

        private void Save()
        {
            var json = JsonSerializer.Serialize(_items, _jsonSerializerOptions);

            File.WriteAllText(_filePath, json);
        }

        private void Load()
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);

                var items = JsonSerializer.Deserialize<ObservableCollection<ServerContainer>>(json);

                if (items != null)
                {
                    _items.CollectionChanged -= OnCollectionChanged;

                    _items.Clear();

                    foreach (var i in items)
                    {
                        _items.Add(i);
                    }

                    _items.CollectionChanged += OnCollectionChanged;
                }
            }
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            Save();
        }
    }
}
