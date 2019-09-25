using System;
using System.Net;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;

//Networking
using Hazel;
using Hazel.Udp;

//Config
using IniParser;
using IniParser.Model;

//Compression and Serialization
using NetStack.Compression;
using NetStack.Serialization;

namespace MayaVerseNetworkingClient1_5
{
    class Client
    {
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
        /// Initial configuration
        /// </summary>
        static Connection connection;
        static Boolean DEBUG = true;
        static String UID = String.Empty;
        static String AvatarName = String.Empty;
        static String AvatarPassword = String.Empty;
        static String ServerIP = String.Empty;
        static int ServerPort = 4296;
        static Dictionary<int, string> DictObjects = new Dictionary<int, string>();
        static BitBuffer data = new BitBuffer(1024);

        private static Vector3 lastPosition = new Vector3(0, 0, 0);
        private static Quaternion lastRotation = new Quaternion(1, 1, 1, 1);
        private static Vector3 VelocityDefaultZero = new Vector3(0, 0, 0);

        /// <summary>
        /// The entry point of the program, where the program control starts and ends.
        /// </summary>
        /// <param name="args">The command-line arguments.</param>
        public static void Main(string[] args)
        {
            //Intialize lastRotation to Quaternion Identity
            lastRotation = Quaternion.Identity;
            //Read Initial Config of Client
            //Create an instance of a ini file parser
            FileIniDataParser fileIniData = new FileIniDataParser();
            //Parse the ini file
            IniData parsedData = fileIniData.ReadFile("HazelUDPTestClient.ini");

            ServerIP = parsedData["ServerConfig"]["ServerIP"];
            //https://www.cambiaresearch.com/articles/68/convert-string-to-int-in-csharp
            ServerPort = System.Convert.ToInt32(parsedData["ServerConfig"]["ServerPort"]);

            Console.WriteLine("ServerIP: " + parsedData["ServerConfig"]["ServerIP"]);
            Console.WriteLine("ServerPort: " + parsedData["ServerConfig"]["ServerPort"]);

            NetworkEndPoint endPoint = new NetworkEndPoint(ServerIP, ServerPort, IPMode.IPv4);

            connection = new UdpClientConnection(endPoint);

            connection.DataReceived += DataReceived;
            connection.Disconnected += ServerDisconnectHandler;

            try
            {
                Console.WriteLine("Connecting!");
                connection.Connect();

                //Send single login message
                {
                    //Login
                    //AvatarName = "Vytek75";
                    AvatarName = parsedData["User"]["UserLogin"];
                    //AvatarPassword = "test1234!";
                    AvatarPassword = parsedData["User"]["UserPassword"];
                    //Send LOGIN Command to 
                    SendMessageToServer(CommandType.LOGIN);
                    Console.WriteLine("Send to: " + connection.EndPoint.ToString());
                }

                ConsoleKeyInfo cki;
                // Prevent example from ending if CTL+C is pressed.
                Console.TreatControlCAsInput = true;

                Console.WriteLine("Press any combination of CTL, ALT, and SHIFT, and a console key.");
                Console.WriteLine("Press the Escape (Esc) key to quit: \n");
                do
                {
                    cki = Console.ReadKey();
                    Console.Write(" --- You pressed ");
                    if ((cki.Modifiers & ConsoleModifiers.Alt) != 0) Console.Write("ALT+");
                    if ((cki.Modifiers & ConsoleModifiers.Shift) != 0) Console.Write("SHIFT+");
                    if ((cki.Modifiers & ConsoleModifiers.Control) != 0) Console.Write("CTL+");
                    Console.WriteLine(cki.Key.ToString());
                    if (cki.Key.ToString().ToLower() == "r")
                    {
                        //https://gateway.ipfs.io/ipfs/QmerSDvd9PTgcbTAz68rL1ujeCZakhdLeAUpdcfdkyhqyx
                        //RezObject("QmerSDvd9PTgcbTAz68rL1ujeCZakhdLeAUpdcfdkyhqyx", UID, true);
                    }
                    if (cki.Key.ToString().ToLower() == "u")
                    {
                        //https://gateway.ipfs.io/ipfs/QmerSDvd9PTgcbTAz68rL1ujeCZakhdLeAUpdcfdkyhqyx
                        //DeRezObject("QmerSDvd9PTgcbTAz68rL1ujeCZakhdLeAUpdcfdkyhqyx", UID, true, (ushort)DictObjects.Count);
                    }
                    if (cki.Key.ToString().ToLower() == "m")
                    {
                        System.Numerics.Vector3 NewOnePosition = new Vector3(1, 2, 3);
                        SendMessage(SendType.SENDTOOTHER, PacketId.OBJECT_MOVE, 1, AvatarName, true, NewOnePosition, lastRotation, VelocityDefaultZero);
                    }
                    if (cki.Key.ToString().ToLower() == "n")
                    {
                        System.Numerics.Vector3 NewOnePosition = new Vector3(3, 2, 1);
                        SendMessage(SendType.SENDTOOTHER, PacketId.OBJECT_MOVE, 1, AvatarName, true, NewOnePosition, lastRotation, VelocityDefaultZero);
                    }
                    if (cki.Key.ToString().ToLower() == "i")
                    {
                        System.Numerics.Vector3 NewOnePosition = new Vector3(3, 2, 1);
                        SendMessage(SendType.SENDTOOTHER, PacketId.OBJECT_MOVE, 2, AvatarName, true, NewOnePosition, lastRotation, VelocityDefaultZero);
                    }
                } while (cki.Key != ConsoleKey.Escape);

                connection.Close();
                Environment.Exit(0);
            }
            catch (Hazel.HazelException ex)
            {
                Console.Error.WriteLine("Error: " + ex.Message + " from " + ex.Source);
                connection.Close();
                Environment.Exit(1);
            }
        }

        /// <summary>
        /// Datas the received.
        /// </summary>
        /// <param name="sender">Sender.</param>
        /// <param name="args">Arguments.</param>
        private static void DataReceived(object sender, DataReceivedEventArgs args)
        {
            Console.WriteLine("Received (" + string.Join<byte>(", ", args.Bytes) + ") from " + connection.EndPoint.ToString());
            //Decode parse received data
            //Remove first byte (type)
            //https://stackoverflow.com/questions/31550484/faster-code-to-remove-first-elements-from-byte-array
            byte SendTypeBuffer = args.Bytes[0];
            byte[] NewBufferReceiver = new byte[args.Bytes.Length - 1];
            Array.Copy(args.Bytes, 1, NewBufferReceiver, 0, NewBufferReceiver.Length);
            //Check SendType
            if ((SendTypeBuffer == (byte)SendType.SENDTOALL) || (SendTypeBuffer == (byte)SendType.SENDTOOTHER))
            {
                //Deserialize message using NetStack
                //Reset bit buffer for further reusing
                data.Clear();
                data.FromArray(NewBufferReceiver, NewBufferReceiver.Length);

                byte TypeBuffer = data.ReadByte();
                string OwnerPlayer = data.ReadString();
                bool isKine = data.ReadBool();
                uint IDObject = data.ReadUInt();
                CompressedVector3 position = new CompressedVector3(data.ReadUInt(), data.ReadUInt(), data.ReadUInt());
                CompressedQuaternion rotation = new CompressedQuaternion(data.ReadByte(), data.ReadShort(), data.ReadShort(), data.ReadShort());
                //Read Vector3 Compress Velocity
                //ushort compressedVelocityX = HalfPrecision.Compress(speed);
                Vector3 VelocityReceived = new Vector3(HalfPrecision.Decompress(data.ReadUShort()), HalfPrecision.Decompress(data.ReadUShort()), HalfPrecision.Decompress(data.ReadUShort()));

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

                //Show DATA received
                if (DEBUG)
                {
                    Console.WriteLine("ID RECEIVED: " + IDObject.ToString());
                    Console.WriteLine("TYPE RECEIVED: " + TypeBuffer.ToString());
                    Console.WriteLine("UID RECEIVED: " + OwnerPlayer);
                    Console.WriteLine("IsKINE RECEIVED: " + isKine.ToString());
                    Console.WriteLine("POS RECEIVED: " + decompressedPosition.X.ToString() + ", " + decompressedPosition.Y.ToString() + ", " + decompressedPosition.Z.ToString());
                    Console.WriteLine("ROT RECEIVED: " + decompressedRotation.X.ToString() + ", " + decompressedRotation.Y.ToString() + ", " + decompressedRotation.Z.ToString() + ", " + decompressedRotation.W.ToString());
                    Console.WriteLine("VEL RECEIVED: " + VelocityReceived.X.ToString() + ", " + VelocityReceived.Y.ToString() + ", " + VelocityReceived.Z.ToString());
                }

                if ((byte)PacketId.PLAYER_JOIN == TypeBuffer)
                {
                    Console.WriteLine("Add new Player!");
                    //Code for new Player
                    //Spawn something? YES
                    //Using Dispatcher? NO
                    //PlayerSpawn
                    SendMessage(SendType.SENDTOOTHER, PacketId.PLAYER_SPAWN, 0, UID + ";" + AvatarName, true, lastPosition, lastRotation, VelocityDefaultZero);
                    //TODO: Using Reliable UDP??
                }
                else if ((byte)PacketId.OBJECT_MOVE == TypeBuffer)
                {
                    Console.WriteLine("OBJECT MOVE");
                }
                else if ((byte)PacketId.PLAYER_MOVE == TypeBuffer)
                {
                    Console.WriteLine("PLAYER MOVE");
                }
                else if ((byte)PacketId.PLAYER_SPAWN == TypeBuffer)
                {
                    Console.WriteLine("PLAYER SPAWN");
                }
                else if ((byte)PacketId.OBJECT_SPAWN == TypeBuffer)
                {
                    Console.WriteLine("OBJECT SPAWN");
                    //Rez Object Received
                    //RezObject(OwnerPlayer.Split(';')[1], OwnerPlayer.Split(';')[0], false);
                }
                else if ((byte)PacketId.OBJECT_UNSPAWN == TypeBuffer)
                {
                    Console.WriteLine("OBJECT UNSPAWN");
                    //De Rez Object Received
                    //DeRezObject(OwnerPlayer.Split(';')[1], OwnerPlayer.Split(';')[0], false, ObjectReceived.ID);
                }
            }
            else if (SendTypeBuffer == (byte)SendType.SENDTOSERVER)
            {
                //Deserialize message using NetStack
                //Reset bit buffer for further reusing
                data.Clear();
                data.FromArray(NewBufferReceiver, NewBufferReceiver.Length);

                byte CommandTypeBuffer = data.ReadByte();
                string Answer = data.ReadString();

                // Check if bit buffer is fully unloaded
                Console.WriteLine("Bit buffer is empty: " + data.IsFinished);

                if ((byte)CommandType.LOGIN == CommandTypeBuffer)
                {
                    if (Answer != String.Empty)
                    {
                        UID = Answer;
                        //Set UID for Your Avatar ME
                        //UnityMainThreadDispatcher.Instance().Enqueue(SetUIDInMainThread(HMessageReceived.Answer));
                        Console.WriteLine("UID RECEIVED: " + Answer);
                        //PLAYER_JOIN MESSAGE (SENDTOOTHER)
                        SendMessage(SendType.SENDTOOTHER, PacketId.PLAYER_JOIN, 0, UID + ";" + AvatarName, true, lastPosition, lastRotation, VelocityDefaultZero);
                        //TO DO: Using Reliable UDP??
                    }
                    else
                    {
                        Console.WriteLine("UID RECEIVED is EMPTY (NOT VALID PASSWORD): " + Answer);
                        //Disconnect
                        if (connection != null)
                        {
                            Console.WriteLine("DisConnecting from: " + connection.EndPoint.ToString());
                            connection.Close();
                        }
                    }
                }
                else if ((byte)CommandType.DISCONNECTEDCLIENT == CommandTypeBuffer)
                {
                    //Debug Disconnected UID
                    Console.WriteLine("UID RECEIVED and TO DESTROY: " + Answer);
                }
            }
            args.Recycle();
        }

        /// <summary>
        /// Sends the message.
        /// </summary>
        /// <param name="SType">ST ype.</param>
        /// <param name="Type">Type.</param>
        /// <param name="IDObject">IDO bject.</param>
        /// <param name="OwnerPlayer">Owner player.</param>
        /// <param name="isKine">If set to <c>true</c> is kine.</param>
        /// <param name="Pos">Position.</param>
        /// <param name="Rot">Rot.</param>
        public static void SendMessage(SendType SType, PacketId Type, ushort IDObject, string OwnerPlayer, bool isKine, Vector3 Pos, Quaternion Rot, Vector3 Vel)
        {
            byte TypeBuffer = 0;
            byte STypeBuffer = 0;

            //Choose who to send message
            switch (SType)
            {
                case SendType.SENDTOALL:
                    STypeBuffer = 0;
                    break;
                case SendType.SENDTOOTHER:
                    STypeBuffer = 1;
                    break;
                case SendType.SENDTOSERVER:
                    STypeBuffer = 2;
                    break;
                default:
                    STypeBuffer = 0;
                    break;
            }
            //Console.WriteLine("SENDTYPE SENT: " + STypeBuffer); //DEBUG

            //Choose type message (TO Modify)
            switch (Type)
            {
                case PacketId.PLAYER_JOIN:
                    TypeBuffer = 0;
                    break;
                case PacketId.OBJECT_MOVE:
                    TypeBuffer = 1;
                    break;
                case PacketId.PLAYER_SPAWN:
                    TypeBuffer = 2;
                    break;
                case PacketId.OBJECT_SPAWN:
                    TypeBuffer = 3;
                    break;
                case PacketId.PLAYER_MOVE:
                    TypeBuffer = 4;
                    break;
                case PacketId.MESSAGE_SERVER:
                    TypeBuffer = 5;
                    break;
                default:
                    TypeBuffer = 1;
                    break;
            }
            //Debug.Log("TYPE SENT: " + TypeBuffer); //DEBUG

            //SEND USING NETSTACK SERIALIZATION
            // Create a new BoundedRange array for Vector3 position, each entry has bounds and precision
            BoundedRange[] worldBounds = new BoundedRange[3];

            worldBounds[0] = new BoundedRange(-50f, 50f, 0.05f); // X axis
            worldBounds[1] = new BoundedRange(0f, 25f, 0.05f); // Y axis
            worldBounds[2] = new BoundedRange(-50f, 50f, 0.05f); // Z axis

            //Convert from HazelUDPTestClient.Vector3 at System.Numerics.Vector3
            System.Numerics.Vector3 InternalPos = new System.Numerics.Vector3(Pos.X, Pos.Y, Pos.Z);
            //Compress position data
            CompressedVector3 compressedPosition = BoundedRange.Compress(InternalPos, worldBounds);

            // Read compressed data
            Console.WriteLine("Compressed position - X: " + compressedPosition.x + ", Y:" + compressedPosition.y + ", Z:" + compressedPosition.z);

            //Convert from HazelUDPTestClient.Quaternion at System.Numerics.Quaternion
            System.Numerics.Quaternion InternalRot = new System.Numerics.Quaternion(Rot.X, Rot.Y, Rot.Z, Rot.W);
            //Compress rotation data
            CompressedQuaternion compressedRotation = SmallestThree.Compress(InternalRot);

            //Read compressed data
            Console.WriteLine("Compressed rotation - M: " + compressedRotation.m + ", A:" + compressedRotation.a + ", B:" + compressedRotation.b + ", C:" + compressedRotation.c);

            //Add and compress Vector3 Velocity
            ushort compressedVelocityX = HalfPrecision.Compress(Vel.X);
            ushort compressedVelocityY = HalfPrecision.Compress(Vel.Y);
            ushort compressedVelocityZ = HalfPrecision.Compress(Vel.Z);

            //Reset bit buffer for further reusing
            data.Clear();

            data.AddByte((byte)TypeBuffer)
                .AddString(OwnerPlayer)
                .AddBool(isKine)
                .AddUInt(IDObject)
                .AddUInt(compressedPosition.x)
                .AddUInt(compressedPosition.y)
                .AddUInt(compressedPosition.z)
                .AddByte(compressedRotation.m)
                .AddInt(compressedRotation.a)
                .AddInt(compressedRotation.b)
                .AddInt(compressedRotation.c)
                .AddUShort(compressedVelocityX)
                .AddUShort(compressedVelocityY)
                .AddUShort(compressedVelocityZ);

            Console.WriteLine("BitBuffer: " + data.Length.ToString());

            //byte[] BufferNetStack = new byte[data.Length+1];
            //https://discordapp.com/channels/515987760281288707/515987760281288711/527744788745814028
            //MA soprattutto: https://discordapp.com/channels/515987760281288707/515987760281288711/536428267851350017
            //Okay guys, after some debugging I've found the mistake in the original BitBuffer implementation, 
            //Alex forgot to check index boundaries during conversion so this is why + 4 bytes was required for shifting.
            //Now it's fixed and no longer needed I hope
            //https://github.com/nxrighthere/NetStack/commit/f381a88751fa0cb72af2cad7652a973d570d3dda
            byte[] BufferNetStack = new byte[data.Length];

            data.ToArray(BufferNetStack);

            //SEND MESSAGE
            // Add type!
            //https://stackoverflow.com/questions/5591329/c-sharp-how-to-add-byte-to-byte-array
            byte[] newArray = new byte[BufferNetStack.Length + 1];
            BufferNetStack.CopyTo(newArray, 1);
            newArray[0] = STypeBuffer;
            connection.SendBytes(newArray, SendOption.None); //WARNING: ALL MESSAGES ARE NOT RELIABLE!

            if (DEBUG)
            {
                Console.WriteLine("Data Lenghts: " + newArray.Length.ToString());
                Console.WriteLine("Data Lenghts NetStack: " + BufferNetStack.Length.ToString());
                PrintByteArray(BufferNetStack);
                Console.WriteLine("Message sent!");
            }
        }

        /// <summary>
        /// Sends the message to server.
        /// </summary>
        /// <param name="Command">Command.</param>
        public static void SendMessageToServer(CommandType Command)
        {
            //Insert login and password
            string UIDBuffer = AvatarName + ";" + AvatarPassword;
            Console.WriteLine("AvatarName: " + UIDBuffer.Split(';')[0]);
            Console.WriteLine("AvatarPassword: " + UIDBuffer.Split(';')[1]);
            //https://stackoverflow.com/questions/2235683/easiest-way-to-parse-a-comma-delimited-string-to-some-kind-of-object-i-can-loop

            //Reset bit buffer for further reusing
            data.Clear();
            //Serialization
            data.AddByte((byte)Command)
                .AddString(UIDBuffer);

            byte[] BufferNetStack = new byte[data.Length];
            data.ToArray(BufferNetStack);

            //SEND MESSAGE
            // Add type!
            //https://stackoverflow.com/questions/5591329/c-sharp-how-to-add-byte-to-byte-array
            byte[] newArray = new byte[BufferNetStack.Length + 1];
            BufferNetStack.CopyTo(newArray, 1);
            newArray[0] = (byte)SendType.SENDTOSERVER;
            connection.SendBytes(newArray, SendOption.Reliable);
            if (DEBUG)
            {
                Console.WriteLine("Data Lenghts: " + newArray.Length.ToString());
                Console.WriteLine("Data Lenghts NetStack: " + BufferNetStack.Length.ToString());
                Console.WriteLine("Data Lenghts BitBuffer: " + data.Length.ToString());
                PrintByteArray(BufferNetStack);
                Console.WriteLine("Message sent!");
            }
        }

        /// <summary>
        /// Servers the disconnect handler.
        /// </summary>
        /// <param name="sender">Sender.</param>
        /// <param name="args">Arguments.</param>
        private static void ServerDisconnectHandler(object sender, DisconnectedEventArgs args)
        {
            Connection connection = (Connection)sender;
            Console.WriteLine("Server connection at " + connection.EndPoint + " lost");
            connection = null;
            args.Recycle();
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
    }
}
