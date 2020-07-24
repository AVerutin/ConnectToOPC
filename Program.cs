using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using System.Diagnostics;

namespace ConnectToOPC
{
    class Program
    {
        private static ApplicationConfiguration _config;
        private static Session _session;
        private static bool _haveAppCertificate;
        private static string _endpointUrl = "opc.tcp://192.168.11.90:49320";
        private static Subscription _subscription;
        private static List<MonitoredItem> _items;

        static async void Run(string[] args)
        {
            Console.WriteLine("Запуск приложения...");
            //foreach (string arg in args)
            //{
            //      Обработка параметров коммандной строки
            //      
            //}

            Dictionary<string, string> items = GetItems();

            try
            {
                // Подключение к OPC-серверу и создание текущей сессии
                await OpenSession(_endpointUrl);
                
                // Добавление списка полученных тегов в подписку
                foreach (KeyValuePair<string, string> item in items)
                {
                    // Создание списка отслеживаемых тегов
                    AddItem(item.Key, item.Value);

                    // Однократное чтение значений выбранных тегов
                    // DataValue val = ReadValue(item.Value);
                    // Console.WriteLine($"{item.Key}: {val.Value}");
                }

                // Обзор списка узлов в категории OBJECTS
                // BrowseRoot();
                // List<Node> nodeTree = GetTree();

                // Чтение значение определенного тега
                // DataValue val = ReadValue("ns=2;s=LEVEL.ДСП.номер плавки");
                // Console.WriteLine($"Номер плавки: {val.Value}");

                // Открытие подписки на изменение значения тегов из списка
                Subscribe();

                // Цикл обработки событий
                // Console.WriteLine("Running...Press any key to exit...");
                Console.ReadKey(true);

            }
            catch (Exception e)
            {
                // Не удалось подключиться к OPC-серверу
                Console.WriteLine($"Не удалось установить подключение с {_endpointUrl}: {e.Message}");
            }
        }

        private static Dictionary<string, string> GetItems()
        {
            string filePath = System.IO.Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName) + @".\params.txt";
            Dictionary<string, string> pars = new Dictionary<string, string>();
            Dictionary<string, string> items = new Dictionary<string, string>();

            // Формат файла с настройками : 
            // <ИмяПараметра>:<ЗначениеПараметра>
            try
            {
                using (StreamReader sr = new StreamReader(filePath, System.Text.Encoding.Default))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        string[] par = line.Split(':');
                        pars.Add(par[0], par[1]);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Не удалось открыть файл параметров");
                Console.ReadKey(true);
                throw ex;
            }

            // Получаем адрес подключения к серверу и порт
            // и заполняем список контролируемых тегов
            foreach (KeyValuePair<string, string> par in pars)
            {
                if (par.Key == "endpoint")
                {
                    // Это адрес сервера
                    _endpointUrl = "opc.tcp://" + par.Value;
                }
                else if (par.Key == "port")
                {
                    // Это номер порта
                    _endpointUrl += ":" + par.Value;
                }
                else
                {
                    // Это тег
                    items.Add(par.Key, par.Value);
                }
            }

            return items;
        }

        public static DataValue ReadValue(string nodeID)
        {
            DataValue value = _session.ReadValue(nodeID);

            return value;
        }

        public static async Task OpenSession(string ep)
        {
            // Console.WriteLine("Create an Application Configuration.");
            _config = new ApplicationConfiguration()
            {
                ApplicationName = "Console OPC-Client",
                ApplicationType = ApplicationType.Client,
                ApplicationUri = "urn:localhost:OPCFoundation:SampleClient",
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = "Directory",
                        StorePath = "./OPC Foundation/CertificateStores/MachineDefault",
                        SubjectName = Utils.Format("CN={0}, DC={1}", "Console OPC-Client", Utils.GetHostName())
                    },
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = "Directory",
                        StorePath = "./OPC Foundation/CertificateStores/UA Applications",
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = "Directory",
                        StorePath = "./OPC Foundation/CertificateStores/UA Certificate Authorities",
                    },
                    RejectedCertificateStore = new CertificateTrustList
                    {
                        StoreType = "Directory",
                        StorePath = "./OPC Foundation/CertificateStores/RejectedCertificates",
                    },
                    NonceLength = 32,
                    AutoAcceptUntrustedCertificates = true
                },
                TransportConfigurations = new TransportConfigurationCollection(),
                TransportQuotas = new TransportQuotas { OperationTimeout = 15000 },
                ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60000 }
            };
            await _config.Validate(ApplicationType.Client);

            _haveAppCertificate = _config.SecurityConfiguration.ApplicationCertificate.Certificate != null;

            if (_haveAppCertificate && _config.SecurityConfiguration.AutoAcceptUntrustedCertificates)
            {
                _config.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);
            }

            if (!_haveAppCertificate)
            {
                Console.WriteLine("    WARN: отсутствует сертификат приложения, подключение не защищено.");
            }

            Uri endpointURI = new Uri(ep);
            var endpointCollection = DiscoverEndpoints(_config, endpointURI, 10);
            var selectedEndpoint = SelectUaTcpEndpoint(endpointCollection, _haveAppCertificate);
            var endpointConfiguration = EndpointConfiguration.Create(_config);
            var endpoint = new ConfiguredEndpoint(selectedEndpoint.Server, endpointConfiguration);
            endpoint.Update(selectedEndpoint);

            _session = await Session.Create(_config, endpoint, true, "Console OPC Client", 60000, null, null);

            // Console.WriteLine("Create a subscription with publishing interval of 1 second.");
            _subscription = new Subscription(_session.DefaultSubscription) { PublishingInterval = 1000 };

            // _items = new List<MonitoredItem>;
        }

        private static void AddItem(string name, string addr)
        {
            if (_items == null)
            {
                _items = new List<MonitoredItem>(1);
            }

            // Создать переменню типа MonitoredItem
            var item = new MonitoredItem(_subscription.DefaultItem)
            {
                DisplayName = name,
                StartNodeId = addr
            };
            _items.Add(item);
        }

        private static void Subscribe()
        {
            // Console.WriteLine("Add a list of items (server current time and status) to the subscription.");
            _items.ForEach(i => i.Notification += OnNotification);
            _subscription.AddItems(_items);

            // Console.WriteLine("Add the subscription to the session.");
            _session.AddSubscription(_subscription);
            _subscription.Create();
        }

        private static void BrowseRoot()
        {
            // Console.WriteLine("Browse the OPC UA server namespace.");
            ReferenceDescriptionCollection references;
            Byte[] continuationPoint;

            references = _session.FetchReferences(ObjectIds.ObjectsFolder);

            _session.Browse(
                null,
                null,
                ObjectIds.ObjectsFolder,
                0u,
                BrowseDirection.Forward,
                ReferenceTypeIds.HierarchicalReferences,
                true,
                (uint)NodeClass.Variable | (uint)NodeClass.Object | (uint)NodeClass.Method,
                out continuationPoint,
                out references);

            Console.WriteLine(" DisplayName, BrowseName, NodeClass");
            foreach (var rd in references)
            {
                Console.WriteLine("{0}, {1}, {2}", rd.DisplayName, rd.BrowseName, rd.NodeClass);
                ReferenceDescriptionCollection nextRefs;
                byte[] nextCp;
                _session.Browse(
                    null,
                    null,
                    ExpandedNodeId.ToNodeId(rd.NodeId, _session.NamespaceUris),
                    0u,
                    BrowseDirection.Forward,
                    ReferenceTypeIds.HierarchicalReferences,
                    true,
                    (uint)NodeClass.Variable | (uint)NodeClass.Object | (uint)NodeClass.Method,
                    out nextCp,
                    out nextRefs);

                foreach (var nextRd in nextRefs)
                {
                    // Если nextRd.NodeClass == Object, то получить все его референсы и найти внутри них все параметры
                    var nodeClass = nextRd.NodeClass;
                    Console.WriteLine("   + {0}, {1}, {2}", nextRd.DisplayName, nextRd.BrowseName, nextRd.NodeClass);
                }
            }
        }

        private static List<Node> GetFolder (ReferenceDescription folder)
        {
            List<Node> nodes = new List<Node>();
            ReferenceDescriptionCollection nextRefs;
            byte[] nextCp;
            _session.Browse(
                    null,
                    null,
                    ExpandedNodeId.ToNodeId(folder.NodeId, _session.NamespaceUris),
                    0u,
                    BrowseDirection.Forward,
                    ReferenceTypeIds.HierarchicalReferences,
                    true,
                    (uint)NodeClass.Variable | (uint)NodeClass.Object | (uint)NodeClass.Method,
                    out nextCp,
                    out nextRefs);

            foreach (var item in nextRefs)
            {
                Node node = new Node();
                if (item.NodeClass == NodeClass.Variable)
                {
                    node.SetName(item.BrowseName.ToString());
                    node.SetNodeId(item.NodeId.ToString());
                    DataValue val = ReadValue(item.NodeId.ToString());
                    node.SetValue(val.ToString());
                    nodes.Add(node);
                }
            }

            return nodes;
        }

        private static List<Node> GetTree()
        {
            // Получение дерева тегов OPC-сервера
            // Node node;
            List<Node> tree = new List<Node>();
            ReferenceDescriptionCollection references;
            Byte[] continuationPoint;
            references = _session.FetchReferences(ObjectIds.ObjectsFolder);

            _session.Browse(
                null,
                null,
                ObjectIds.ObjectsFolder,
                0u,
                BrowseDirection.Forward,
                ReferenceTypeIds.HierarchicalReferences,
                true,
                (uint)NodeClass.Variable | (uint)NodeClass.Object | (uint)NodeClass.Method,
                out continuationPoint,
                out references);

            foreach (var rd in references)
            {
                if (rd.NodeClass == NodeClass.Object)
                {
                    List<Node> folder = GetFolder(rd);
                    // tree.Add(folder);
                }

            }

            return tree;
        }

        private static void OnNotification(MonitoredItem item, MonitoredItemNotificationEventArgs e)
        {
            foreach (var value in item.DequeueValues())
            {
                Console.WriteLine("{0}: {1}, {2}, {3}", item.DisplayName, value.Value, value.SourceTimestamp, value.StatusCode);
            }
        }

        private static void CertificateValidator_CertificateValidation(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            Console.WriteLine($"Принятый сертификат: {e.Certificate.Subject}");
            e.Accept = (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted);
        }

        private static EndpointDescriptionCollection DiscoverEndpoints(ApplicationConfiguration config, Uri discoveryUrl, int timeout)
        {
            // use a short timeout.
            EndpointConfiguration configuration = EndpointConfiguration.Create(config);
            configuration.OperationTimeout = timeout;

            using (DiscoveryClient client = DiscoveryClient.Create(
                discoveryUrl,
                EndpointConfiguration.Create(config)))
            {
                try
                {
                    EndpointDescriptionCollection endpoints = client.GetEndpoints(null);
                    ReplaceLocalHostWithRemoteHost(endpoints, discoveryUrl);
                    return endpoints;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Не удалось найти OPC-сервер по адресу: {discoveryUrl}");
                    Console.WriteLine($"Ошибка = {e.Message}");
                    throw e;
                }
            }
        }

        private static EndpointDescription SelectUaTcpEndpoint(EndpointDescriptionCollection endpointCollection, bool haveCert)
        {
            EndpointDescription bestEndpoint = null;
            foreach (EndpointDescription endpoint in endpointCollection)
            {
                if (endpoint.TransportProfileUri == Profiles.UaTcpTransport)
                {
                    if (bestEndpoint == null ||
                        haveCert && (endpoint.SecurityLevel > bestEndpoint.SecurityLevel) ||
                        !haveCert && (endpoint.SecurityLevel < bestEndpoint.SecurityLevel))
                    {
                        bestEndpoint = endpoint;
                    }
                }
            }
            return bestEndpoint;
        }

        private static void ReplaceLocalHostWithRemoteHost(EndpointDescriptionCollection endpoints, Uri discoveryUrl)
        {
            foreach (EndpointDescription endpoint in endpoints)
            {
                endpoint.EndpointUrl = Utils.ReplaceLocalhost(endpoint.EndpointUrl, discoveryUrl.DnsSafeHost);
                StringCollection updatedDiscoveryUrls = new StringCollection();
                foreach (string url in endpoint.Server.DiscoveryUrls)
                {
                    updatedDiscoveryUrls.Add(Utils.ReplaceLocalhost(url, discoveryUrl.DnsSafeHost));
                }
                endpoint.Server.DiscoveryUrls = updatedDiscoveryUrls;
            }
        }

        static void Main (string[] args)
        {
            Run(args);
        }
    }
}
