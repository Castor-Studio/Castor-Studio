using System.IO;
using System.Text.Json;

namespace CastorApplication.Services.Auth.Storage
{
    public abstract class JsonFileStore<T> where T : new()
    {
        private readonly string _filePath;

        protected JsonFileStore(string filePath)
        {
            _filePath = filePath;
        }

        protected static readonly JsonSerializerOptions
            JsonOptions = new()
            {
                WriteIndented = true
            };

        public T Load()
        {
            try
            {
                if (!File.Exists(_filePath))
                    return new T();

                var json =
                    File.ReadAllText(_filePath);

                return JsonSerializer.Deserialize<T>(
                           json,
                           JsonOptions)
                       ?? new T();
            }
            catch
            {
                return new T();
            }
        }

        public void Save(T data)
        {
            var directory =
                Path.GetDirectoryName(_filePath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json =
                JsonSerializer.Serialize(
                    data,
                    JsonOptions);

            File.WriteAllText(_filePath, json);
        }
    }
}
