// See https://aka.ms/new-console-template for more information
using System;
using Newtonsoft.Json;
using System.IO.Compression;
using System.Xml;
using System.Text.RegularExpressions;
using System.Diagnostics;


class Program
{
    static async Task Main(string[] args)
    {
        string url = "https://fias.nalog.ru/WebServices/Public/GetLastDownloadFileInfo";
        string rootFolderName = "gar_delta_xml";
        Archive archive = new Archive(rootFolderName);

        await archive.GetJson(url);
        if (archive.DictionaryResponse != null && archive.DictionaryResponse.ContainsKey("GarXMLDeltaURL"))
        {
            
            await archive.GetFile(archive.DictionaryResponse["GarXMLDeltaURL"].ToString(), "gar_delta_xml.zip");
            
            archive.ExtractArchive("gar_delta_xml.zip");

            archive.ReadXmlFile();

            string date = archive.GetVersionBase();

            List<Dictionary<string, string>> levels = archive.ReadXmlFile();

            Queue<string> paths = archive.SearchDirectories();
            List<Dictionary<string, string>> listOfDictionaries = new List<Dictionary<string, string>>();



            while (paths.Count > 0)
            {
                string path = paths.Dequeue();
                archive.TakeInfo(path, levels, ref listOfDictionaries);
            }

            var sortedGrouped = listOfDictionaries
                .GroupBy(d => d["LEVEL"])
                .OrderBy(g => g.Key)
                .Select(g => new
                {
                    Level = g.Key,
                    Items = g.OrderBy(dict => dict["NAME"])
                });

            archive.MakeHtml(sortedGrouped, date);
                        
            Console.ReadKey();
        }
        else
        {
            Console.WriteLine("Не верный api");
            Console.ReadKey();
        }

    }

    class Archive
    {
        private string BaseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        public Dictionary<string, object> DictionaryResponse;
        public string RootFolderName = "gar_delta_xml";

        public Archive(string rootFolderName)
        {
            RootFolderName = rootFolderName;
        }

        //получение json
        public async Task GetJson(string url)
        {
            using (HttpClient client = new HttpClient())

            {
                try
                {
                    HttpResponseMessage response = await client.GetAsync(url);
                    response.EnsureSuccessStatusCode();

                    string jsonResponse = await response.Content.ReadAsStringAsync();

                    DictionaryResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonResponse);

                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }

        //скачать файл
        public async Task GetFile(string url, string outPutPath)
        {
            if (File.Exists(outPutPath)) 
            {
                try
                {
                    File.Delete(outPutPath);
                }
                catch (Exception e) 
                {
                    Console.WriteLine("Не удалось удалить старый архив, убедитесь что он у вас не открыт");
                    Console.WriteLine(e.ToString());
                }
            }

            using (HttpClient client = new HttpClient())
            {
                try
                {
                    Console.WriteLine("Пакет изменений скачивается ...");
                    HttpResponseMessage response = await client.GetAsync(url);
                    response.EnsureSuccessStatusCode();
                    using (var filestream = new FileStream(outPutPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
                    {

                        await response.Content.CopyToAsync(filestream);
                    }

                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Произошла ошибка во время скачивания архива: {ex.ToString()}");
                }
            }

        }

        //разархивировать файл
        public void ExtractArchive(string nameZipFile)
        {
            string zipPath = Path.Combine(BaseDirectory, nameZipFile);
            string extractPath = Path.Combine(BaseDirectory, RootFolderName);
            try
            {
                Console.WriteLine("Распаковка архива ...");
                if (File.Exists(zipPath))
                {
                    if (Directory.Exists(extractPath))
                    {
                        Directory.Delete(extractPath, true);
                    }
                    ZipFile.ExtractToDirectory(zipPath, extractPath);
                    Console.WriteLine("Архив успешно распакован!");
                }
                else
                {
                    Console.WriteLine("Архив не найден!");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Ошибка при распаковке архива!");
                Console.WriteLine(e.ToString());
            }
        }

        //получить нужные level
        public List<Dictionary<string, string>> ReadXmlFile()
        {
            string directoryPath = Path.Combine(BaseDirectory, RootFolderName);
            List<string> targetNames = new List<string>
        {
            "помещение",
            "помещения в пределах помещения",
            "машино-место",
            "земельный участок"
        };
            List<Dictionary<string, string>> levels = new List<Dictionary<string, string>>();

            foreach (var file in Directory.GetFiles(directoryPath, "AS_OBJECT_LEVELS*.xml"))
            {
                try
                {
                    XmlDocument xmlDoc = new XmlDocument();
                    xmlDoc.Load(file);


                    foreach (XmlNode objectLevel in xmlDoc.GetElementsByTagName("OBJECTLEVEL"))
                    {
                        string name = objectLevel.Attributes["NAME"]?.Value.ToLower();
                        string levelStr = objectLevel.Attributes["LEVEL"]?.Value;

                        if (!targetNames.Contains(name) && int.TryParse(levelStr, out _))
                        {
                            Dictionary<string, string> dictionary = new Dictionary<string, string>
                            {
                                { "NAME", name },
                                { "LEVEL", levelStr }
                            };
                            levels.Add(dictionary);
                        }
                    }
                    break;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Ошибка при чтении файла: {e.Message}");
                }
            }
            return levels;
        }

        //получить директории всех папок
        public Queue<string> SearchDirectories()
        {
            Queue<string> directoryPathsFromXmlFiles = new Queue<string>();
            try
            {                
                string directoryPath = Path.Combine(BaseDirectory, RootFolderName);

                string[] directories = Directory.GetDirectories(directoryPath);
                foreach (string directory in directories)
                {
                    directoryPathsFromXmlFiles.Enqueue(directory);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
            return directoryPathsFromXmlFiles;
        }

        //получить данные из файла
        public void TakeInfo(string path, List<Dictionary<string, string>> levels, ref List<Dictionary<string, string>> listOfDictionaries)
        {
            foreach (var file in Directory.GetFiles(path, "AS_ADDR_OBJ_2*.xml"))
            {
                try
                {
                    XmlDocument xmlDoc = new XmlDocument();
                    xmlDoc.Load(file);

                    foreach (XmlNode objectLevel in xmlDoc.GetElementsByTagName("OBJECT"))
                    {
                        string name = objectLevel.Attributes["NAME"]?.Value;
                        string typename = objectLevel.Attributes["TYPENAME"]?.Value;
                        string isActive = objectLevel.Attributes["ISACTIVE"]?.Value;
                        string level = objectLevel.Attributes["LEVEL"]?.Value;

                        if (levels.Any(l => l["LEVEL"] == level) && isActive == "1")
                        {
                            var levelName = levels.Where(l => l["LEVEL"] == level).First();
                            Dictionary<string, string> dictionary = new Dictionary<string, string>
                            {
                                {"LEVEL", levelName["NAME"]},
                                {"NAME", name},
                                {"TYPENAME", typename}
                            };
                            listOfDictionaries.Add(dictionary);
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }
        }

        //получить версию базы
        public string GetVersionBase()
        {
            string directoryPath = Path.Combine(BaseDirectory, RootFolderName);
            string date = @"\d{4}\.\d{2}\.\d{2}";
            try
            {
                using(StreamReader reader = new StreamReader(Path.Combine(directoryPath, "version.txt")))
                {
                    string version;
                    while((version = reader.ReadLine()) != null)
                    {
                        Match match = Regex.Match(version, date);
                        if (match.Success) 
                        {
                            return match.Value;
                        }
                    }                    
                }
            }
            catch (Exception e)
            { Console.WriteLine(e.Message); }
            DateTime currenDate = DateTime.Now;
            return currenDate.ToString("yyyy.MM.dd");
        }

        //вывод данных в html
        public void MakeHtml(IEnumerable<dynamic>sortedGrouped, string date)
        {
            string html = $@"
        <html>
        <head>
            <style>
                body {{
                    font-family: Arial, sans-serif;
                    font-size: 14px;
                    background-color: #f4f4f4;
                }}
                h2, h1 {{
                    margin-top: 30px;
                    text-align: center;
                }}
                table {{
                    width: 50%;
                    margin: 20px auto;
                    border-collapse: collapse;
                }}
                th, td {{
                    border: 1px solid #dddddd;
                    padding: 8px;
                    text-align: left;
                    width: 50%;
                }}
                th {{
                    background-color: #4CAF50;
                    color: white;
                }}
                tr:hover {{
                    background-color: #f1f1f1;
                }}
            </style>
        </head>
        <body>
        <h1>Отчет по добавленным адресным объектам за {date} </h1>";

            foreach (var group in sortedGrouped)
            {
                string result = string.IsNullOrEmpty(group.Level) ? string.Empty : char.ToUpper(group.Level[0]) + group.Level.Substring(1);
                html += $"<h2>{result}</h2>";
                html += "<table border='1'><tr><th>Тип Объекта</th><th>Наименование</th></tr>";
                foreach (var item in group.Items)
                {
                    html += $"<tr><td>{item["TYPENAME"]}</td><td>{item["NAME"]}</td></tr>";
                }
                html += "</table>";
            }
            html += "</body></html>";

            File.WriteAllText("report.html", html);
            Console.WriteLine("Отчет создан!");

            Process.Start(new ProcessStartInfo("report.html") { UseShellExecute = true });
        }

    }
}



