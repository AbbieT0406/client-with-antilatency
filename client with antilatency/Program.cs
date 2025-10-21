using Antilatency.Alt.Environment.Selector;
using Antilatency.Alt.Tracking;
using Antilatency.DeviceNetwork;
using antilatency_getter;
using Google.FlatBuffers;
using HIVE.Commons.Flatbuffers.Generated;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Linq;

internal class Program
{
    private static volatile bool _running = true; //changes depending on wether or not the program is running
    private static readonly ConcurrentDictionary<ulong, AltData> _altDevices = new(); //stores info about all connected devices/coontrollers
    private static StreamWriter _writer; //writes data to nodeData.txt
    private static bool _useBlueEnvironment = true; //switches depening on wether the tracking environment is blue (true) or green (false)

    // Data center connection settings
    private static string _serverIP = "127.0.0.1";
    private static int _port = 6000;
    private static TcpClient _tcpClient;
    private static NetworkStream _networkStream;
    private static bool _connectedToDataCenter = false;
    private static readonly object _connectionLock = new object(); //makes sure only part of the proram can use the data center at a time

    // Rate limiting
    private static readonly Dictionary<ulong, DateTime> _lastSendTimes = new(); //remembers when each robot was last called
    private static readonly TimeSpan _minSendInterval = TimeSpan.FromMilliseconds(100); //wait time between calls to the same robot

    // AltData class definition, this stores all info about each tracking device/controller
    private class AltData
    {
        public ulong Id { get; }
        public ITrackingCotask Cotask { get; }
        public NodeHandle Node { get; }
        public bool IsBlueEnvironment { get; }

        //constructor
        public AltData(ulong id, ITrackingCotask cotask, NodeHandle node, bool isBlueEnvironment)
        {
            Id = id;
            Cotask = cotask;
            Node = node;
            IsBlueEnvironment = isBlueEnvironment;
        }
    }

    static void Main(string[] args)
    {
        Console.WriteLine("Press 'q' to quit, 's' for status, 't' to toggle environment, 'c' to connect/disconnect from data center");

        AppDomain.CurrentDomain.ProcessExit += (s, e) => StopApplication();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            StopApplication(); //shutdown handler
        };

        try
        {
            // Initialize file writer
            _writer = new StreamWriter("nodeData.txt", append: true); //opend the nodeData.txt
            _writer.WriteLine($"=== Session started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==="); //writes to the text file

            DataCollector();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal error: {ex.Message}"); //error handling
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
        finally
        {
            StopApplication(); //cleanup
        }
    }

    private static void StopApplication()
    {
        _running = false;

        // Clean up data center connection
        DisconnectFromDataCenter();

        // Clean up all tasks
        foreach (var device in _altDevices.Values)
        {
            device.Cotask?.Dispose();
        }
        _altDevices.Clear();
        //writes to the text file
        _writer?.WriteLine($"=== Session ended at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        _writer?.Close();
        _writer?.Dispose();
        Console.WriteLine("Application stopped.");
    }

    private static void DataCollector()
    {
        // Load libraries
        using Antilatency.Alt.Tracking.ILibrary trackingLibrary = Antilatency.Alt.Tracking.Library.load();
        using Antilatency.Alt.Environment.Selector.ILibrary environmentSelectorLibrary = Antilatency.Alt.Environment.Selector.Library.load();

        // Create network and tracking constructor
        using INetwork network = AntilatencyHandler.CreateNetwork();
        using ITrackingCotaskConstructor trackingCotaskConstructor = trackingLibrary.createTrackingCotaskConstructor();

        // Create environments
        using Antilatency.Alt.Environment.IEnvironment blueEnvironment = AntilatencyHandler.CreateEnvironmentBlue(environmentSelectorLibrary);
        using Antilatency.Alt.Environment.IEnvironment greenEnvironment = AntilatencyHandler.CreateEnvironmentGreen(environmentSelectorLibrary);

        uint previousUpdateId = 0;
        Console.WriteLine("Waiting for tracking data...");

        while (_running)
        {
            // Handle user input
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true);
                switch (key.KeyChar)
                {
                    case 'q': //quits
                    case 'Q':
                        _running = false;
                        break;
                    case 's': //shows the status of the onnected device/controller
                    case 'S':
                        PrintStatus();
                        break;
                    case 't': //swtiches between blue and green environments
                    case 'T':
                        _useBlueEnvironment = !_useBlueEnvironment;
                        Console.WriteLine($"Environment switched to: {(_useBlueEnvironment ? "BLUE" : "GREEN")}");
                        ReconnectAllDevices(network, trackingCotaskConstructor,
                            _useBlueEnvironment ? blueEnvironment : greenEnvironment,
                            _useBlueEnvironment);
                        break;
                    case 'c': //connects/disconnects to the data center
                    case 'C':
                        if (_connectedToDataCenter)
                        {
                            DisconnectFromDataCenter();
                        }
                        else
                        {
                            ConnectToDataCenter();
                        }
                        break;
                }
            }

            
            uint currentUpdateId = network.getUpdateId(); //checks for new devices
            if (previousUpdateId != currentUpdateId) //if a new id is detected
            {
                previousUpdateId = currentUpdateId;
                DiscoverAndAddDevices(network, trackingCotaskConstructor, //connect and start tracking the new controllers/devices
                    _useBlueEnvironment ? blueEnvironment : greenEnvironment,
                    _useBlueEnvironment);
            }

            // Process tracking data for all connected devices
            ProcessTrackingData(_useBlueEnvironment);

            Thread.Sleep(16);
        }
    }

    private static void ConnectToDataCenter()
    {
        lock (_connectionLock)
        {
            if (_connectedToDataCenter) return; //checks if already connected

            try
            {
                _tcpClient = new TcpClient(); //creates a TCP connection
                _tcpClient.Connect(_serverIP, _port);
                _networkStream = _tcpClient.GetStream();

                // Send magic number
                uint magic = 0x23476945;
                byte[] magicBytes = BitConverter.GetBytes(magic);
                _networkStream.Write(magicBytes, 0, magicBytes.Length);

                _connectedToDataCenter = true;

                // Start background thread to read responses from data center
                Thread responseThread = new Thread(ReadDataCenterResponses);
                responseThread.IsBackground = true;
                responseThread.Start();

                Console.WriteLine($"Connected to data center at {_serverIP}:{_port}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to connect to data center: {ex.Message}");
                _connectedToDataCenter = false;
            }
        }
    }

    private static void ReadDataCenterResponses()
    {
        while (_running && _connectedToDataCenter && _tcpClient?.Connected == true)
        {
            try
            {
                if (_networkStream?.DataAvailable == true) //checks for data
                {
                    // Read response length prefix (4 bytes)
                    byte[] lengthBuffer = new byte[4];
                    int bytesRead = _networkStream.Read(lengthBuffer, 0, 4);

                    if (bytesRead == 4)
                    {
                        int responseLength = BitConverter.ToInt32(lengthBuffer, 0); //converts into int representing the message size

                        if (responseLength > 0 && responseLength < 100000) // only process messages that are a reasonable size
                        {
                            // Read the response data
                            byte[] responseData = new byte[responseLength]; //creates a buffer for the full message
                            int totalRead = 0;

                            while (totalRead < responseLength && _running && _connectedToDataCenter) //reads until it gets all the expected bytes
                            {
                                int read = _networkStream.Read(responseData, totalRead, responseLength - totalRead); 
                                if (read == 0) break;
                                totalRead += read;
                            }

                            if (totalRead == responseLength)
                            {
                                Console.WriteLine($"Received {responseLength} bytes from data center");
                                ProcessDataCenterResponse(responseData); //processes the full message
                            }
                        }
                    }
                }
                else
                {
                    Thread.Sleep(50); 
                }
            }
            catch (Exception ex)
            {
                if (_connectedToDataCenter)
                {
                    Console.WriteLine($"ERROR reading from data center: {ex.Message}");
                    DisconnectFromDataCenter();
                }
                break;
            }
        }
    }

    private static void ProcessDataCenterResponse(byte[] responseData)
    {
        try
        {
            var byteBuffer = new ByteBuffer(responseData);

            // Try to parse as State message first
            try
            {
                var state = HIVE.Commons.Flatbuffers.Generated.State.GetRootAsState(byteBuffer);
                Console.WriteLine($"Data center State: {state.PayloadLength} payload(s), {responseData.Length} bytes");

                for (int i = 0; i < state.PayloadLength; i++) //goes through each payload in the state message
                {
                    var payload = state.Payload(i);
                    if (payload.HasValue)
                    {
                        var dataSegment = payload.Value.GetDataBytes();
                        if (dataSegment.HasValue)
                        {
                            var segment = dataSegment.Value;
                            byte[] payloadBytes = segment.Array != null
                                ? segment.Array.Skip(segment.Offset).Take(segment.Count).ToArray()
                                : Array.Empty<byte>();

                            Console.WriteLine($"Payload {i + 1}: {payloadBytes.Length} bytes");

                            // Try to parse payload as Entity
                            try //tires to read the load as an entity
                            {
                                var payloadBuffer = new ByteBuffer(payloadBytes);
                                var entity = Entity.GetRootAsEntity(payloadBuffer);

                                Console.WriteLine($"Entity Type: {entity.EntityType}");

                                if (entity.EntityType == EntityUnion.Robot) //extracts and displays robot data if the entitiy is identified as a robot
                                {
                                    var robot = entity.Entity_AsRobot();
                                    Console.WriteLine($" ");
                                    Console.WriteLine($"ROBOT DATA:");
                                    Console.WriteLine($"ID: {robot.Id}");
                                    Console.WriteLine($"Name: {robot.Name}");
                                    Console.WriteLine($"Subscription: {robot.Subscription}");
                                    Console.WriteLine($"Rate: {robot.Rate}");
                                    Console.WriteLine($"Color: 0x{robot.Colour:X8}");
                                    Console.WriteLine($" ");

                                    if (robot.BoundingBox.HasValue) //gets bounding box center and size (the position and size of the robot)
                                    {
                                        var bb = robot.BoundingBox.Value;
                                        if (bb.Centre.HasValue)
                                        {
                                            var centre = bb.Centre.Value;
                                            Console.WriteLine($"Position: X={centre.X:F3}, Y={centre.Y:F3}, Z={centre.Z:F3}");
                                        }
                                        if (bb.Dimensions.HasValue)
                                        {
                                            var dim = bb.Dimensions.Value;
                                            Console.WriteLine($"Size: W={dim.X:F3}, H={dim.Y:F3}, D={dim.Z:F3}");
                                        }
                                    }
                                }
                                else if (entity.EntityType == EntityUnion.Environment) //does this if the data is envirnment data
                                {
                                    Console.WriteLine($"ENVIRONMENT DATA");
                                }
                                else //unknown entity type
                                {
                                    Console.WriteLine($"Unknown Entity: {entity.EntityType}");
                                }
                            }
                            catch (Exception entityEx)
                            {
                                // if it doesnt notice an entity, it tries to parse as Robot directly
                                try
                                {
                                    var robotBuffer = new ByteBuffer(payloadBytes);
                                    var robot = Robot.GetRootAsRobot(robotBuffer);
                                    Console.WriteLine($"DIRECT ROBOT DATA:");
                                    Console.WriteLine($"ID: {robot.Id}");
                                    Console.WriteLine($"Name: {robot.Name}");
                                    Console.WriteLine($"Subscription: {robot.Subscription}");
                                }
                                catch
                                {
                                    // if its still not a Robot , raw info is displayed
                                    Console.WriteLine($"Raw payload (first 50 bytes): {BitConverter.ToString(payloadBytes.Take(50).ToArray())}");
                                    if (payloadBytes.Length > 0)
                                    {
                                        string asString = System.Text.Encoding.UTF8.GetString(payloadBytes.Take(100).ToArray());
                                        Console.WriteLine($"As text: {asString.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\0", "\\0")}");
                                    }
                                }
                            }
                        }
                    }
                }
                return;
            }
            catch (Exception stateEx)
            {
                // Not a State message
            }

            // Try to parse as direct Entity
            try
            {
                var entity = Entity.GetRootAsEntity(byteBuffer);
                Console.WriteLine($"Direct Entity: {entity.EntityType}, {responseData.Length} bytes");

                if (entity.EntityType == EntityUnion.Robot)
                {
                    var robot = entity.Entity_AsRobot();
                    Console.WriteLine($"ROBOT: {robot.Name} (ID: {robot.Id})");
                }
                return;
            }
            catch
            {
                // Not a direct Entity
            }

            // Try to parse as direct Robot
            try
            {
                var robot = Robot.GetRootAsRobot(byteBuffer);
                Console.WriteLine($"Direct Robot: {robot.Name} (ID: {robot.Id}), {responseData.Length} bytes");
                return;
            }
            catch
            {
                // Not a direct Robot
            }

            // Final fallback - show raw data
            Console.WriteLine($"Unknown message format: {responseData.Length} bytes");
            Console.WriteLine($"Hex (first 100 bytes): {BitConverter.ToString(responseData.Take(100).ToArray())}");

            if (responseData.Length > 0)
            {
                string asString = System.Text.Encoding.UTF8.GetString(responseData.Take(100).ToArray());
                Console.WriteLine($"As text: {asString.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\0", "\\0")}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR processing data center response: {ex.Message}");
            Console.WriteLine($"Raw data: {BitConverter.ToString(responseData.Take(50).ToArray())}...");
        }
    }

    private static void DisconnectFromDataCenter()
    {
        lock (_connectionLock) //prevents other parts of the program from using the connection while disconnecting from the data center
        {
            _networkStream?.Close(); //closes the data stream
            _networkStream?.Dispose(); //releases all sources used by the stream
            _networkStream = null;

            _tcpClient?.Close(); //closes the network connection
            _tcpClient?.Dispose(); //releases all sources used by the connection
            _tcpClient = null;

            _connectedToDataCenter = false; //sets connection status to disconnected
            Console.WriteLine("Disconnected from data center");
        }
    }

    private static void SendRobotDataToDataCenter(ulong deviceId, Antilatency.Math.float3 position, bool isBlue)
    {
        // Rate limiting
        if (_lastSendTimes.ContainsKey(deviceId) &&
            (DateTime.Now - _lastSendTimes[deviceId]) < _minSendInterval)
        {
            return;
        }

        if (!_connectedToDataCenter || _tcpClient?.Connected != true || _networkStream?.CanWrite != true)
        {
            return;
        }

        try
        {
            // Build robot request using FlatBuffer
            var robotRequestBuilder = new FlatBufferBuilder(256);

            // Create robot name
            var robotName = $"ROBOT_{deviceId:X}";
            var nameOffset = robotRequestBuilder.CreateString(robotName);

            // Create bounding box with current position
            var centreT = new Vec3T { X = position.x, Y = position.y, Z = position.z };
            var dimensionsT = new Vec3T { X = 1.0f, Y = 1.0f, Z = 1.0f };
            var rotationT = new Vec4T { W = 0, X = 0, Y = 0, Z = 1 };

            // Build the bounding box
            var boundingBoxOffset = BoundingBox.CreateBoundingBox(
                robotRequestBuilder,
                centreT,
                dimensionsT,
                rotationT,
                false
            );

            // Build the robot object
            Robot.StartRobot(robotRequestBuilder);
            Robot.AddId(robotRequestBuilder, deviceId);
            Robot.AddName(robotRequestBuilder, nameOffset);
            Robot.AddSubscription(robotRequestBuilder, (ushort)20);
            Robot.AddRate(robotRequestBuilder, SubscriptionRate.Full);
            Robot.AddBoundingBox(robotRequestBuilder, boundingBoxOffset);
            Robot.AddColour(robotRequestBuilder, 0xFF0000FFu);
            var robotOffset = Robot.EndRobot(robotRequestBuilder);

            // Wrap the robot in an entity container
            var entityOffset = Entity.CreateEntity(robotRequestBuilder, EntityUnion.Robot, robotOffset.Value);
            robotRequestBuilder.Finish(entityOffset.Value);
            byte[] robotRequestBytes = robotRequestBuilder.SizedByteArray();

            // Create outer message container
            var builder = new FlatBufferBuilder(1024);
            var dataVector = Payload.CreateDataVector(builder, robotRequestBytes);

            // Build payload
            Payload.StartPayload(builder);
            Payload.AddData(builder, dataVector);
            var payloadOffset = Payload.EndPayload(builder);

            // Create state message
            var payloadsVector = HIVE.Commons.Flatbuffers.Generated.State.CreatePayloadVector(builder, new[] { payloadOffset });
            HIVE.Commons.Flatbuffers.Generated.State.StartState(builder);
            HIVE.Commons.Flatbuffers.Generated.State.AddPayload(builder, payloadsVector);
            var stateOffset = HIVE.Commons.Flatbuffers.Generated.State.EndState(builder);

            builder.Finish(stateOffset.Value);
            byte[] stateBytes = builder.SizedByteArray();

            // Send message to server
            byte[] stateLenPrefix = BitConverter.GetBytes(stateBytes.Length);
            _networkStream.Write(stateLenPrefix, 0, stateLenPrefix.Length);
            _networkStream.Write(stateBytes, 0, stateBytes.Length);

            _lastSendTimes[deviceId] = DateTime.Now;

            Console.WriteLine($"Sent robot {deviceId:X} to data center - Position: ({position.x:F3}, {position.y:F3}, {position.z:F3})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR sending robot data to data center: {ex.Message}");
            DisconnectFromDataCenter();
        }
    }

    private static void DiscoverAndAddDevices(
        INetwork network,
        ITrackingCotaskConstructor trackingCotaskConstructor,
        Antilatency.Alt.Environment.IEnvironment environment,
        bool isBlue)
    {
        var nodes = trackingCotaskConstructor.findSupportedNodes(network); //checks for devices/controllers to be used

        if (nodes.Length > 0)
        {
            Console.WriteLine($"Found {nodes.Length} supported nodes");
        }

        foreach (var node in nodes)
        {
            try
            {
                // Get parent node serial number
                var parentNode = network.nodeGetParent(node);
                var serialNo = network.nodeGetStringProperty(parentNode, //reads the devices serial number
                    Antilatency.DeviceNetwork.Interop.Constants.HardwareSerialNumberKey);

                if (string.IsNullOrEmpty(serialNo))
                {
                    Console.WriteLine("Warning: Empty serial number for node");
                    continue;
                }

                var senderId = Convert.ToUInt64(serialNo, 16); //converts the serial number to a numeric id

                // checks if device is already connected
                if (_altDevices.ContainsKey(senderId))
                {
                    if (_altDevices[senderId].IsBlueEnvironment != isBlue)
                    {
                        _altDevices[senderId].Cotask?.Dispose();
                        _altDevices.TryRemove(senderId, out _);
                    }
                    else
                    {
                        continue; //skips if already connected
                    }
                }

                // Start tracking task for this new device
                var trackingCotask = trackingCotaskConstructor.startTask(network, node, environment);

                var altData = new AltData(senderId, trackingCotask, node, isBlue); //creates device info object
                _altDevices[senderId] = altData; //stores in the dvices directory

                Console.WriteLine($"Connected device: {senderId:X} ({(isBlue ? "BLUE" : "GREEN")})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to connect device: {ex.Message}");
            }
        }
    }

    private static void ReconnectAllDevices(
        INetwork network,
        ITrackingCotaskConstructor trackingCotaskConstructor,
        Antilatency.Alt.Environment.IEnvironment environment,
        bool isBlue)
    {
        foreach (var device in _altDevices.Values) //loops through all connected devices and stops tracking tasks
        {
            device.Cotask?.Dispose();
        }
        _altDevices.Clear(); //removes all devices

        DiscoverAndAddDevices(network, trackingCotaskConstructor, environment, isBlue); //find and reconnect all devices and use the new environment
    }

    private static void ProcessTrackingData(bool isBlue)
    {
        var environmentText = isBlue ? "BLUE" : "GREEN"; //set environment text for logs
        var devicesToRemove = new List<ulong>(); //list of failed devices

        foreach (var altData in _altDevices.Values)
        {
            try
            {
                if (altData.Cotask.isTaskFinished()) //checks if device tracking has stopped
                {
                    Console.WriteLine($"Device {altData.Id:X} task finished, removing...");
                    devicesToRemove.Add(altData.Id); //marks for removal
                    continue;
                }

                var trackingState = altData.Cotask.getState(0.03f);

                if (trackingState.stability.stage.value != Antilatency.Alt.Tracking.Stage.Tracking6Dof)
                {
                    if (_altDevices.Count <= 2)
                    {
                        Console.WriteLine($"Device {altData.Id:X}: {trackingState.stability.stage.value} (waiting for 6DoF)");
                    }
                    continue;
                }

                var position = trackingState.pose.position;

                if (position.x == 0 && position.y == 0 && position.z == 0)//ignores positions at origin (0,0,0) because its ususally invalid data
                    continue;

                var dataLine = $"{DateTime.Now:HH:mm:ss.fff} {altData.Id:X} {environmentText} {position.x:F6} {position.y:F6} {position.z:F6}"; //logs a timestamp, device ID and environment to text file
                _writer.WriteLine(dataLine);

                SendRobotDataToDataCenter(altData.Id, position, isBlue); //sends position data to the data center

                if (_altDevices.Count <= 2) //shows position on consle if few devices
                {
                    Console.WriteLine($"📍 Device {altData.Id:X} {environmentText}: ({position.x:F3}, {position.y:F3}, {position.z:F3})");
                }
            }
            catch (Exception ex) //catches device errors and marks for removal
            {
                Console.WriteLine($"Error processing device {altData.Id:X}: {ex.Message}");
                devicesToRemove.Add(altData.Id);
            }
        }

        foreach (var deviceId in devicesToRemove) //removes failed devices
        {
            if (_altDevices.TryRemove(deviceId, out var altData))
            {
                altData.Cotask?.Dispose();
                Console.WriteLine($"Removed device: {deviceId:X}");
            }
        }
    }

    private static void PrintStatus() //self explanatory i wont explain this
    {
        Console.WriteLine($"=== Status ===");
        Console.WriteLine($"Connected devices: {_altDevices.Count}");
        Console.WriteLine($"Current environment: {(_useBlueEnvironment ? "BLUE" : "GREEN")}");
        Console.WriteLine($"Data center connection: {(_connectedToDataCenter ? "CONNECTED" : "DISCONNECTED")}");
        Console.WriteLine($"Running: {_running}");
        Console.WriteLine($"Data file: nodeData.txt");
        Console.WriteLine("Commands: [Q]uit, [S]tatus, [T]oggle environment, [C]onnect/disconnect data center");

        foreach (var device in _altDevices.Values)
        {
            Console.WriteLine($"  Device {device.Id:X} ({(device.IsBlueEnvironment ? "BLUE" : "GREEN")})");
        }
    }
}




