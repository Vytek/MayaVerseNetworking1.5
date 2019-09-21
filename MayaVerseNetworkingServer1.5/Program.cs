using System;
using System.Net;
using System.Collections;
using System.Numerics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text;
using System.IO;
using System.Threading;
using System.Reflection;
using System.Diagnostics;

//Networking
using Hazel;
using Hazel.Udp;

//Access Control
using LiteDB;
using Scrypt;

//Compression and Serialization
using NetStack.Compression;
using NetStack.Serialization;

//Log
using NLog;

namespace MayaVerseNetworkingServer1_5
{
    public class Server
    {
        public int portNumber = 4296;
        public static bool DEBUG = true;
        public bool Running { get; private set; }

        /// <summary>
        /// Users class.
        /// </summary>
        public class Users
        {
            public int Id { get; set; }
            public string UserName { get; set; }
            public string UserPassword { get; set; }
            public bool IsActive { get; set; }
        }

        /// <summary>
        /// Objects class.
        /// </summary>
        public class Objects
        {
            public int Id { get; set; }
            public int IDObject { get; set; }
            public string UID { get; set; }
            public bool isKine { get; set; }
            public float PosX { get; set; }
            public float PosY { get; set; }
            public float PosZ { get; set; }
            public float RotX { get; set; }
            public float RotY { get; set; }
            public float RotZ { get; set; }
            public float RotW { get; set; }
        }

        /// <summary>
        /// Send type.
        /// </summary>
        public enum SendType : byte
        {
            SENDTOALL = 0,
            SENDTOOTHER = 1,
            SENDTOSERVER = 2,
            SENDTOUID = 3 //NOT IMPLEMENTED
        }

        /// <summary>
        /// Packet identifier.
        /// </summary>
        public enum PacketId : byte
        {
            PLAYER_JOIN = 0,
            OBJECT_MOVE = 1,
            PLAYER_SPAWN = 2,
            OBJECT_SPAWN = 3,
            PLAYER_MOVE = 4,
            MESSAGE_SERVER = 5,
            OBJECT_UNSPAWN = 6
        }

        /// <summary>
        /// Command type.
        /// </summary>
		public enum CommandType : byte
        {
            LOGIN = 0,
            DISCONNECTEDCLIENT = 1
        }

        /// <summary>
        /// Client message received.
        /// </summary>
		public struct ClientMessageReceived
        {
            public byte[] MessageBytes;
            public Connection ClientConnected;
            public Hazel.SendOption SOClientConnected;
        };

        //List<Connection> clients = new List<Connection>();
        //https://stackoverflow.com/questions/8629285/how-to-create-a-collection-like-liststring-object
        static List<KeyValuePair<String, Connection>> clients = new List<KeyValuePair<String, Connection>>();
        //Queue Messages
        static ConcurrentQueue<ClientMessageReceived> QueueMessages = new ConcurrentQueue<ClientMessageReceived>();

        static ManualResetEvent _quitEvent = new ManualResetEvent(false);

        //LiteDB connection
        static LiteDatabase db = new LiteDatabase(Path.Combine(AssemblyDirectory, @"UsersObjects.db"));

        //NetStack
        //public static BitBuffer data = new BitBuffer(1024); //*** NOT USING HERE!!! ****

        //Log
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Main class.
        /// </summary>
        class MainClass
        {
            public static void Main(string[] args)
            {
                ThreadPool.QueueUserWorkItem(Server.ConsumerThread);
                Server ServerHazel = new Server();
                ServerHazel.Start();
            }
        }

        /// <summary>
        /// Start this instance.
        /// </summary>
		public void Start()
        {

            //Log
            var config = new NLog.Config.LoggingConfiguration();

            // Targets where to log to: File and Console
            var logfile = new NLog.Targets.FileTarget("logfile") { FileName = "MayaVerse.log" };
            //var logconsole = new NLog.Targets.ConsoleTarget("logconsole");

            // Rules for mapping loggers to targets            
            //config.AddRule(LogLevel.Info, LogLevel.Fatal, logconsole);
            config.AddRule(LogLevel.Info, LogLevel.Fatal, logfile);

            // Apply config           
            NLog.LogManager.Configuration = config;

            //https://stackoverflow.com/questions/2586612/how-to-keep-a-net-console-app-running
            Console.CancelKeyPress += (sender, eArgs) =>
            {
                _quitEvent.Set();
                eArgs.Cancel = true;
            };

            //Connect and create users collection for LiteDB.org
            //Get users collection
            var col = db.GetCollection<Users>("users");

            if (col.Count() == 0)
            {
                ScryptEncoder encoder = new ScryptEncoder();
                string hashsedPassword = encoder.Encode("test1234!");
                //Console.WriteLine(hashsedPassword);
                //Same password
                //string hashsedPassword2 = encoder.Encode("test1234!");
                //Console.WriteLine(hashsedPassword);
                // Create your new customer instance
                var user = new Users
                {
                    UserName = "Vytek75",
                    UserPassword = hashsedPassword,
                    IsActive = true
                };

                // Create unique index in Name field
                col.EnsureIndex(x => x.UserName, true);

                // Insert new customer document (Id will be auto-incremented)
                col.Insert(user);
            }

            NetworkEndPoint endPoint = new NetworkEndPoint(IPAddress.Any, portNumber);
            ConnectionListener listener = new UdpConnectionListener(endPoint);

            Running = true;

            Console.WriteLine("Starting server!");
            Logger.Info("Starting server!");
            System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
            string version = fvi.FileVersion;
            Console.WriteLine("Server Version: " + version);
            Console.WriteLine("DB file path: " + Path.Combine(AssemblyDirectory, @"UsersObjects.db"));
            Console.WriteLine("Server listening on " + (listener as UdpConnectionListener).EndPoint);
            listener.NewConnection += NewConnectionHandler;
            listener.Start();

            _quitEvent.WaitOne();

            //Close Log
            NLog.LogManager.Shutdown();
            //Close all
            listener.Close();
            //Exit 0
            Environment.Exit(0);
        }

        /// <summary>
        /// News the connection handler.
        /// </summary>
        /// <param name="sender">Sender.</param>
        /// <param name="args">Arguments.</param>
		private void NewConnectionHandler(object sender, NewConnectionEventArgs args)
        {
            string UID = RandomIdGenerator.GetBase62(6);
            Console.WriteLine("UID Created: " + UID);
            //https://www.dotnetperls.com/keyvaluepair
            clients.Add(new KeyValuePair<string, Connection>(UID, args.Connection));
            Console.WriteLine("New connection from " + args.Connection.EndPoint.ToString() + " with UID: " + UID);
            args.Connection.DataReceived += this.DataReceivedHandler;
            args.Connection.Disconnected += this.ClientDisconnectHandler;
            args.Recycle();
        }

        /// <summary>
        /// Datas the received handler.
        /// </summary>
        /// <param name="sender">Sender.</param>
        /// <param name="args">Arguments.</param>
        private void DataReceivedHandler(object sender, Hazel.DataReceivedEventArgs args)
        {
            Connection connection = (Connection)sender;
            Console.WriteLine("Received (" + string.Join<byte>(", ", args.Bytes) + ") from " + connection.EndPoint.ToString());
            Console.WriteLine("RecvType: " + args.Bytes.GetValue(0).ToString());
            //Console.WriteLine(((byte)SendType.SENDTOALL).ToString());

            //Create Struct ClientMessageReceived
            ClientMessageReceived NewClientConnected;
            NewClientConnected.ClientConnected = connection;
            NewClientConnected.MessageBytes = args.Bytes;
            NewClientConnected.SOClientConnected = args.SendOption;

            //Add To main Queue
            QueueMessages.Enqueue(NewClientConnected);
            ThreadPool.QueueUserWorkItem(Server.ConsumerThread);
            args.Recycle();
        }

        /// <summary>
        /// Consumers the thread.
        /// </summary>
        /// <param name="arg">Argument.</param>
        private static void ConsumerThread(object arg)
        {
            ClientMessageReceived item;
            //while (true)
            Console.WriteLine("Queue: " + Server.QueueMessages.Count.ToString());
            while (!Server.QueueMessages.IsEmpty)
            {
                bool isSuccessful = Server.QueueMessages.TryDequeue(out item);
                Console.WriteLine("Dequeue: " + isSuccessful);
                if (isSuccessful)
                {
                    //https://stackoverflow.com/questions/943398/get-int-value-from-enum-in-c-sharp
                    //https://msdn.microsoft.com/it-it/library/system.enum.getvalues(v=vs.110).aspx
                    //http://csharp.net-informations.com/statements/enum.htm
                    if (((byte)SendType.SENDTOALL).ToString() == item.MessageBytes.GetValue(0).ToString()) //0
                    {
                        //BROADCAST (SENDTOALL)
                        Console.WriteLine("BROADCAST (SENDTOALL)");
                        //Send data received to all client in List
                        foreach (var conn in Server.clients)
                        {
                            if (true)
                            {
                                conn.Value.SendBytes(item.MessageBytes, item.SOClientConnected);
                                Console.WriteLine("Send to: " + conn.Value.EndPoint.ToString());
                            }

                        }
                    }
                    else if ((byte)SendType.SENDTOOTHER == (byte)item.MessageBytes.GetValue(0)) //1
                    {
                        //BROADCAST (SENDTOOTHER)
                        Console.WriteLine("BROADCAST (SENDTOOTHER)");
                        //Call Objects Table
                        var col = db.GetCollection<Objects>("objects");
                        //Parser Message
                        //Remove first byte (type)
                        //https://stackoverflow.com/questions/31550484/faster-code-to-remove-first-elements-from-byte-array
                        byte STypeBuffer = item.MessageBytes[0];
                        byte[] NewBufferReceiver = new byte[item.MessageBytes.Length - 1];
                        Array.Copy(item.MessageBytes, 1, NewBufferReceiver, 0, NewBufferReceiver.Length);

                        //Deserialize message using NetStack
                        //Reset bit buffer for further reusing
                        BitBuffer data = new BitBuffer(1024);
                        data.FromArray(NewBufferReceiver, NewBufferReceiver.Length);

                        byte TypeBuffer = data.ReadByte();
                        string OwnerPlayer = data.ReadString();
                        bool isKine = data.ReadBool();
                        uint IDObject = data.ReadUInt();
                        CompressedVector3 position = new CompressedVector3(data.ReadUInt(), data.ReadUInt(), data.ReadUInt());
                        CompressedQuaternion rotation = new CompressedQuaternion(data.ReadByte(), data.ReadShort(), data.ReadShort(), data.ReadShort());
                        //Velocity Vector read but non used
                        Vector3 velocity = new Vector3(data.ReadUShort(), data.ReadUShort(), data.ReadUShort());

                        // Check if bit buffer is fully unloaded
                        Console.WriteLine("Bit buffer is empty: " + data.IsFinished);

                        //Decompress Vector and Quaternion
                        //Create a new BoundedRange array for Vector3 position, each entry has bounds and precision
                        BoundedRange[] worldBounds = new BoundedRange[3];

                        worldBounds[0] = new BoundedRange(-50f, 50f, 0.05f); // X axis
                        worldBounds[1] = new BoundedRange(0f, 25f, 0.05f); // Y axis
                        worldBounds[2] = new BoundedRange(-50f, 50f, 0.05f); // Z axis

                        //Decompress position data
                        Vector3 decompressedPosition = BoundedRange.Decompress(position, worldBounds);

                        // Decompress rotation data
                        Quaternion decompressedRotation = SmallestThree.Decompress(rotation);

                        if (Server.DEBUG)
                        {
                            Console.WriteLine("RECEIVED DATA: ");
                            Console.WriteLine("TYPE RECEIVED: " + TypeBuffer.ToString());
                            Console.WriteLine("IDObject RECEIVED: " + IDObject.ToString());
                            Console.WriteLine("UID RECEIVED: " + OwnerPlayer);
                            Console.WriteLine("isKinematic RECEIVED: " + isKine.ToString());
                            Console.WriteLine("POS RECEIVED: " + decompressedPosition.X.ToString() + ", " + decompressedPosition.Y.ToString() + ", " + decompressedPosition.Z.ToString());
                            Console.WriteLine("ROT RECEIVED: " + decompressedRotation.X.ToString() + ", " + decompressedRotation.Y.ToString() + ", " + decompressedRotation.Z.ToString() + ", " + decompressedRotation.W.ToString());
                            Console.WriteLine("PosX: " + decompressedPosition.X);
                            Console.WriteLine("PosY: " + decompressedPosition.Y);
                            Console.WriteLine("PosZ: " + decompressedPosition.Z);
                            //var ReceiveMessageFromGameObjectBuffer = new ReceiveMessageFromGameObject(); //NOT USED!
                        }

                        //Check if ObjectReceived.ID <> 0
                        if (IDObject != 0)
                        {
                            var MVobject = new Objects
                            {
                                IDObject = (int)IDObject,
                                isKine = isKine,
                                PosX = decompressedPosition.X,
                                PosY = decompressedPosition.Y,
                                PosZ = decompressedPosition.Z,
                                RotX = decompressedRotation.X,
                                RotY = decompressedRotation.Y,
                                RotZ = decompressedRotation.Z,
                                RotW = decompressedRotation.W,
                                UID = IDObject.ToString() + ";" + OwnerPlayer
                            };

                            //Debug
                            Console.WriteLine("MVobject PosX: " + MVobject.PosX);
                            Console.WriteLine("MVobject PosY: " + MVobject.PosY);
                            Console.WriteLine("MVobject PosZ: " + MVobject.PosZ);

                            if ((byte)PacketId.OBJECT_SPAWN == TypeBuffer)
                            {
                                // Insert new customer document (Id will be auto-incremented)
                                col.Insert(MVobject);

                                // Create unique index in Name field
                                col.EnsureIndex(x => x.UID, true);
                                Console.WriteLine("OBJECT SPAWN SAVED");
                            }
                            else if ((byte)PacketId.OBJECT_MOVE == TypeBuffer)
                            {
                                //Check if record exist
                                if (col.Count(Query.EQ("UID", IDObject.ToString() + ";" + OwnerPlayer)) == 1)
                                {
                                    //Search and update
                                    // Now, search for document your document
                                    var ObjectsFinded = col.FindOne(x => x.UID == IDObject.ToString() + ";" + OwnerPlayer);
                                    //Update data
                                    ObjectsFinded.isKine = isKine;
                                    ObjectsFinded.PosX = decompressedPosition.X;
                                    ObjectsFinded.PosY = decompressedPosition.Y;
                                    ObjectsFinded.PosZ = decompressedPosition.Z;
                                    ObjectsFinded.RotX = decompressedRotation.X;
                                    ObjectsFinded.RotY = decompressedRotation.Y;
                                    ObjectsFinded.RotZ = decompressedRotation.Z;
                                    ObjectsFinded.RotW = decompressedRotation.W;

                                    //Save data to Objects DB
                                    if (col.Update(ObjectsFinded))
                                    {
                                        Console.WriteLine("UPDATE OBJECT IN DB");
                                    }
                                    else
                                    {
                                        Console.WriteLine("*NOT* UPDATED OBJECT IN DB");
                                    }
                                }
                                else
                                {
                                    col.Insert(MVobject);
                                    //Insert data to Objects DB

                                    col.EnsureIndex(x => x.UID, true);
                                    //Create unique index in Name field
                                    Console.WriteLine("INSERT OBJECT IN DB");
                                }
                                Console.WriteLine("OBJECT MOVE");
                            }
                            else if ((byte)PacketId.OBJECT_UNSPAWN == TypeBuffer)
                            {
                                if (col.Count(Query.EQ("UID", IDObject.ToString() + ";" + OwnerPlayer)) == 1)
                                {
                                    col.Delete(Query.EQ("UID", IDObject.ToString() + ";" + OwnerPlayer));
                                    //Save data to Objects DB
                                    Console.WriteLine("DELETE OBJECT FROM DB");
                                }
                                else
                                {
                                    Console.WriteLine("OBJECT UNSPAWN NOT IN DB"); ;
                                }
                                Console.WriteLine("OBJECT UNSPAWN");
                            }
                        } // END Check ObjectReceived.ID <> 0
                        //Send data received to all other client in List
                        Console.WriteLine("SEND MESSAGE TO OTHER CLIENTS");
                        foreach (var conn in Server.clients)
                        {
                            if (conn.Value != item.ClientConnected) //SENDTOOTHER
                            {
                                conn.Value.SendBytes(item.MessageBytes, item.SOClientConnected);
                                Console.WriteLine("Send to: " + conn.Value.EndPoint.ToString());
                            }

                        }
                    }
                    else if ((byte)SendType.SENDTOSERVER == (byte)item.MessageBytes.GetValue(0)) //2
                    {
                        //FOR NOW ECHO SERVER (SENDTOSERVER)
                        Console.WriteLine("CLIENT TO SERVER (SENDTOSERVER)");
                        //Parser Message
                        //Remove first byte (type)
                        //https://stackoverflow.com/questions/31550484/faster-code-to-remove-first-elements-from-byte-array
                        byte STypeBuffer = item.MessageBytes[0];
                        byte[] NewBufferReceiver = new byte[item.MessageBytes.Length - 1];
                        Array.Copy(item.MessageBytes, 1, NewBufferReceiver, 0, NewBufferReceiver.Length);
                        //Deserialize message using NetStack
                        //Reset bit buffer for further reusing
                        BitBuffer data = new BitBuffer(1024);
                        data.FromArray(NewBufferReceiver, NewBufferReceiver.Length);

                        byte CommandTypeBuffer = data.ReadByte();
                        string Answer = data.ReadString();

                        // Check if bit buffer is fully unloaded
                        Console.WriteLine("Bit buffer is empty: " + data.IsFinished);
                        String UIDBuffer = String.Empty;
                        if (STypeBuffer == 2)
                        {
                            if ((sbyte)CommandType.LOGIN == CommandTypeBuffer)
                            {
                                //Cerca e restituisci il tutto
                                foreach (var conn in Server.clients)
                                {
                                    if (conn.Value == item.ClientConnected) //SENDTOSERVER
                                    {
                                        //DONE: Check here if user exist and password correct
                                        //Get users collection
                                        var col = db.GetCollection<Users>("users");
                                        Console.WriteLine("COMMAND RECEIVED: " + Answer);
                                        //Parse HMessageReceived
                                        string[] words = Answer.Split(';');
                                        //words[0] = Login; words[1] = Password
                                        if (col.Count(Query.EQ("UserName", words[0])) == 1)
                                        {
                                            var results = col.Find(Query.EQ("UserName", words[0]));
                                            string UserPasswordRecord = string.Empty;
                                            foreach (var c in results)
                                            {
                                                Console.WriteLine("#{0} - {1}", c.Id, c.UserName);
                                                UserPasswordRecord = c.UserPassword;
                                            }
                                            //Verify password
                                            ScryptEncoder encoder = new ScryptEncoder();
                                            //Check password
                                            if (encoder.Compare(words[1], UserPasswordRecord))
                                            {
                                                //OK
                                                UIDBuffer = conn.Key;
                                                Console.WriteLine("UID: " + UIDBuffer);
                                            }
                                            else
                                            {
                                                //*NOT* OK
                                                UIDBuffer = string.Empty;
                                                Console.WriteLine("UID: ERROR PASSWORD" + UIDBuffer);
                                            }
                                        }
                                        else
                                        {
                                            UIDBuffer = string.Empty;
                                            Console.WriteLine("UID: USER NOT EXISTS!" + UIDBuffer);
                                        }
                                    }
                                }
                            }
                        }

                        //Reset bit buffer for further reusing
                        data.Clear();
                        data.AddByte((byte)CommandType.LOGIN)
                            .AddString(UIDBuffer);

                        byte[] BufferNetStack = new byte[data.Length];
                        data.ToArray(BufferNetStack);
                        data.Clear();

                        //SEND MESSAGE
                        // Add type!
                        //https://stackoverflow.com/questions/5591329/c-sharp-how-to-add-byte-to-byte-array
                        byte[] newArray = new byte[BufferNetStack.Length + 1];
                        BufferNetStack.CopyTo(newArray, 1);
                        newArray[0] = (byte)SendType.SENDTOSERVER;
                        item.ClientConnected.SendBytes(newArray, item.SOClientConnected);
                        if (DEBUG)
                        {
                            Console.WriteLine("Data Lenghts: " + newArray.Length.ToString());
                            Console.WriteLine("Data Lenghts NetStack: " + BufferNetStack.Length.ToString());
                            Console.WriteLine("Data Lenghts BitBuffer: " + data.Length.ToString());
                            Console.WriteLine("Message sent!");
                        }
                        Console.WriteLine("Send to: " + item.ClientConnected.EndPoint.ToString());
                        //HERE SEND TO ALL CLIENTS OBJECTS DB
                        //DONE: Add code to send all clients
                        Console.WriteLine("SEND ALL OBJECTS TO CLIENT");
                        //Call Objects Table
                        var col_objects = db.GetCollection<Objects>("objects");
                        //Recovers all objects in the table
                        var results_objects = col_objects.Find(Query.GT("_id", 0));
                        //Foreach send them to the client connected
                        foreach (var o in results_objects)
                        {
                            Console.WriteLine("SEND IDOBJECT: " + o.IDObject.ToString());
                            //SEND USING NETSTACK SERIALIZATION
                            // Create a new BoundedRange array for Vector3 position, each entry has bounds and precision
                            BoundedRange[] worldBounds = new BoundedRange[3];

                            worldBounds[0] = new BoundedRange(-50f, 50f, 0.05f); // X axis
                            worldBounds[1] = new BoundedRange(0f, 25f, 0.05f); // Y axis
                            worldBounds[2] = new BoundedRange(-50f, 50f, 0.05f); // Z axis

                            //Convert from HazelUDPTestClient.Vector3 at System.Numerics.Vector3
                            System.Numerics.Vector3 InternalPos = new System.Numerics.Vector3(o.PosX, o.PosY, o.PosZ);
                            //Compress position data
                            CompressedVector3 compressedPosition = BoundedRange.Compress(InternalPos, worldBounds);

                            // Read compressed data
                            Console.WriteLine("Compressed position - X: " + compressedPosition.x + ", Y:" + compressedPosition.y + ", Z:" + compressedPosition.z);

                            //Convert from HazelUDPTestClient.Quaternion at System.Numerics.Quaternion
                            System.Numerics.Quaternion InternalRot = new System.Numerics.Quaternion(o.RotX, o.RotY, o.RotZ, o.RotW);
                            // Compress rotation data
                            CompressedQuaternion compressedRotation = SmallestThree.Compress(InternalRot);

                            // Read compressed data
                            Console.WriteLine("Compressed rotation - M: " + compressedRotation.m + ", A:" + compressedRotation.a + ", B:" + compressedRotation.b + ", C:" + compressedRotation.c);

                            //Add Velocity Vector (0,0,0)
                            Vector3 velocity = Vector3.Zero;

                            //Reset bit buffer for further reusing
                            data.Clear();
                            //Serialization
                            data.AddByte((byte)PacketId.OBJECT_MOVE)
                                .AddString(o.UID.Split(';')[1]) //OwnerPlayer
                                .AddBool(o.isKine)
                                .AddUInt((uint)o.IDObject)
                                .AddUInt(compressedPosition.x)
                                .AddUInt(compressedPosition.y)
                                .AddUInt(compressedPosition.z)
                                .AddByte(compressedRotation.m)
                                .AddShort(compressedRotation.a)
                                .AddShort(compressedRotation.b)
                                .AddShort(compressedRotation.c)
                                .AddUShort(HalfPrecision.Compress(velocity.X))
                                .AddUShort(HalfPrecision.Compress(velocity.Y))
                                .AddUShort(HalfPrecision.Compress(velocity.Z));

                            Console.WriteLine("BitBuffer: " + data.Length.ToString());

                            byte[] BufferNetStackObject = new byte[data.Length];
                            data.ToArray(BufferNetStackObject);
                            data.Clear();
                            //https://discordapp.com/channels/515987760281288707/515987760281288711/527744788745814028
                            //MA soprattutto: https://discordapp.com/channels/515987760281288707/515987760281288711/536428267851350017
                            //Okay guys, after some debugging I've found the mistake in the original BitBuffer implementation, 
                            //Alex forgot to check index boundaries during conversion so this is why + 4 bytes was required for shifting.
                            //Now it's fixed and no longer needed I hope
                            //https://github.com/nxrighthere/NetStack/commit/f381a88751fa0cb72af2cad7652a973d570d3dda

                            //SEND MESSAGE
                            // Add type!
                            //https://stackoverflow.com/questions/5591329/c-sharp-how-to-add-byte-to-byte-array
                            byte[] newArrayObject = new byte[BufferNetStackObject.Length + 1]; //Create +1 NewArrayObject
                            BufferNetStackObject.CopyTo(newArrayObject, 1); //Coping start from position 1 (NOT 0)
                            newArrayObject[0] = (byte)SendType.SENDTOOTHER; //Coping position 0 byte SendType
                            item.ClientConnected.SendBytes(newArrayObject, item.SOClientConnected); //Send new packet

                            if (DEBUG)
                            {
                                Console.WriteLine("Data Lenghts: " + newArray.Length.ToString());
                                Console.WriteLine("Data Lenghts NetStack: " + BufferNetStack.Length.ToString());
                                Console.WriteLine("Message sent!");
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Clients the disconnect handler.
        /// </summary>
        /// <param name="sender">Sender.</param>
        /// <param name="args">Arguments.</param>
		private void ClientDisconnectHandler(object sender, DisconnectedEventArgs args)
        {
            Connection connection = (Connection)sender;
            Console.WriteLine("Connection from " + connection.EndPoint + " lost");
            String UIDBuffer = String.Empty;
            //Cerca e restituisci il tutto
            foreach (var conn in clients)
            {
                if (conn.Value == connection) //SENDTOSERVER
                {
                    UIDBuffer = conn.Key;
                    Console.WriteLine("UID TO DESTROY: " + UIDBuffer);
                }

            }

            //https://stackoverflow.com/posts/1608949/revisions //Debug
            //Delete client disconnected
            clients.RemoveAll(item => item.Value.Equals(connection));

            BitBuffer data = new BitBuffer(1024);
            data.AddByte((byte)CommandType.DISCONNECTEDCLIENT)
                .AddString(UIDBuffer);

            Console.WriteLine("BitBuffer: " + data.Length.ToString());

            byte[] BufferNetStack = new byte[data.Length];
            data.ToArray(BufferNetStack);
            data.Clear();

            //Add type!
            //https://stackoverflow.com/questions/5591329/c-sharp-how-to-add-byte-to-byte-array
            byte[] newArray = new byte[BufferNetStack.Length + 1];
            BufferNetStack.CopyTo(newArray, 1);
            newArray[0] = (byte)SendType.SENDTOSERVER;
            foreach (var conn in clients)
            {
                conn.Value.SendBytes(newArray, SendOption.Reliable);
                Console.WriteLine("Send to: " + conn.Value.EndPoint.ToString());
            }
            args.Recycle();
        }


        /// <summary>
        /// Return path of main assembly
        /// </summary>
        public static string AssemblyDirectory
        {
            get
            {
                string codeBase = Assembly.GetExecutingAssembly().CodeBase;
                UriBuilder uri = new UriBuilder(codeBase);
                string path = Uri.UnescapeDataString(uri.Path);
                return Path.GetDirectoryName(path);
            }
        }

        /// <summary>
        /// Shutdown this instance.
        /// </summary>
		public void Shutdown()
        {
            if (Running)
            {
                Running = false;
                Console.WriteLine("Shutting down the Hazel Server...");
            }
        }

        //https://stackoverflow.com/questions/10940883/c-converting-byte-array-to-string-and-printing-out-to-console
        /// <summary>
        /// Prints the byte array.
        /// </summary>
        /// <param name="bytes">Bytes.</param>
        public static void PrintByteArray(byte[] bytes)
        {
            var sb = new StringBuilder("new byte[] { ");
            foreach (var b in bytes)
            {
                sb.Append(b + ", ");
            }
            sb.Append("}");
            Console.WriteLine(sb.ToString());
        }

        //https://stackoverflow.com/posts/9543797/revisions
        //https://stackoverflow.com/questions/9543715/generating-human-readable-usable-short-but-unique-ids?answertab=votes#tab-top
        /// <summary>
        /// Random identifier generator.
        /// </summary>
        public static class RandomIdGenerator
        {
            private static char[] _base62chars =
                "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz"
                .ToCharArray();

            private static Random _random = new Random();

            public static string GetBase62(int length)
            {
                var sb = new StringBuilder(length);

                for (int i = 0; i < length; i++)
                    sb.Append(_base62chars[_random.Next(62)]);

                return sb.ToString();
            }

            public static string GetBase36(int length)
            {
                var sb = new StringBuilder(length);

                for (int i = 0; i < length; i++)
                    sb.Append(_base62chars[_random.Next(36)]);

                return sb.ToString();
            }
        }
    }
}
