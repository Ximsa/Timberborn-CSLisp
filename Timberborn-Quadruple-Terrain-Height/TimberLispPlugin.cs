using BencodeNET.Objects;
using BencodeNET.Parsing;
using BepInEx;
using BepInEx.Logging;
using CSLisp.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace TimberbornTimberLisp
{
    [BepInPlugin("org.bepinex.plugins.timberlisp", "Timberlisp", "0.0.1")]
    public class TimberLisp : BaseUnityPlugin
    {
        public void Awake()
        {
            var server = new NreplServer();
            Thread t = new Thread(server.Run);
            t.Start();
        }
    }
    public class MyLogger : ILogger
    {
        readonly ManualLogSource logger;
        public MyLogger(string name)
        {
            logger = Logger.CreateLogSource(name);
        }
        bool ILogger.EnableParsingLogging => false;

        bool ILogger.EnableInstructionLogging => false;

        bool ILogger.EnableStackLogging => false;

        void ILogger.Log(params object[] args)
        {
            var strings = args.Select(obj => (obj == null) ? "null" : obj.ToString());
            var message = string.Join(" ", strings);
            logger.Log(BepInEx.Logging.LogLevel.Message, message);
        }
        public void Log(params object[] args) 
        {
            this.Log(args);
        }
    }

    public class NreplServer
    {
        readonly ManualLogSource logger;
        readonly Context lispContext;
        public NreplServer()
        {
            logger = Logger.CreateLogSource("cs-repl");
            lispContext = new Context(logger: new MyLogger("cs-repl-code"));
        }
        private BDictionary ResponseFor(BDictionary oldMessage, BDictionary message) 
        {
            BString session = "none";
            BString id = "unknown";
            if (oldMessage.TryGetValue("session", out IBObject rawSession))
                session = (BString)rawSession;
            if (oldMessage.TryGetValue("id", out IBObject rawId))
                id = rawId.ToString();
            message["session"] = session;
            message["id"] = id;
            return message;
        }
        private void Send(NetworkStream stream, BDictionary message) 
        {
            Byte[] rawMessage = message.EncodeAsBytes();
            stream.Write(rawMessage, 0, rawMessage.Length);
            stream.Flush();
        }
        private void SendException(NetworkStream stream, BDictionary message, Exception e) 
        {
            BDictionary response = new BDictionary
            {
                ["ex"] = new BString(e.Message),
                ["status"] = new BList(new String[] { "done" })
            };
            Send(stream, ResponseFor(message, response));
        }
        private void EvalMsg(NetworkStream stream, BDictionary message) 
        {
            string value = "";
            if (message.TryGetValue("code", out IBObject rawCode))
            {
                string code = ((BString)rawCode).ToString();
                var output = lispContext.CompileAndExecute(code).Select(r => r.output);
                value = String.Join(" ", lispContext.CompileAndExecute(code).Select(r => r.output));
            }
            BDictionary firstResponse = new BDictionary
            {
                ["ns"] = new BString(String.Join(" ", lispContext.CompileAndExecute("(package-get)").Select(r => r.output))),
                ["value"] = new BString(value)
            };
            Send(stream, ResponseFor(message, firstResponse));
            BDictionary secondResponse = new BDictionary
            {
                ["status"] = new BList(new String[] { "done" })
            };
            Send(stream, ResponseFor(message, secondResponse));
        }

        private String RegisterSession(NetworkStream stream, BDictionary message)
        {
            String newSessionId = Guid.NewGuid().ToString();
            BDictionary response = new BDictionary
            {
                ["new-session"] = (BString)newSessionId,
                ["status"] = new BList(new String[] { "done" })
            };
            Send(stream, ResponseFor(message, response));
            return newSessionId;
        }
        public BDictionary ReadMessage(NetworkStream stream, BencodeParser parser)
        {
            Byte[] buffer = new byte[1024];
            List<Byte> rawMessage = new List<Byte>();
            do
            {
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                for (int i = 0; i < bytesRead; i++)
                    rawMessage.Add(buffer[i]);
            } while (stream.DataAvailable);
            return parser.Parse<BDictionary>(rawMessage.ToArray());
        }

        public void Run() 
        {
            TcpListener server = null;
            BencodeParser parser = new BencodeParser();
            String sessionId = "pre-init"; // repl session id
            try
            {
                server = new TcpListener(IPAddress.Parse("127.0.0.1"), 1111);
                server.Start();
                logger.LogMessage("starting....");
                using TcpClient client = server.AcceptTcpClient();
                // connected
                logger.LogMessage("connected!");
                while (true)
                {
                    NetworkStream stream = client.GetStream();
                    BDictionary message = ReadMessage(stream, parser);
                    if (message.TryGetValue("op", out IBObject value))
                    {
                        switch (((BString)value).ToString())
                        {
                            case "clone": // register a new session
                                sessionId = RegisterSession(stream, message);
                                break;
                            case "eval": // evaluate message
                                try
                                {
                                    EvalMsg(stream, message);
                                }
                                catch (Exception e)
                                {
                                    logger.LogWarning(e.Message);
                                    SendException(stream, message, e);
                                }
                                break;
                            case "describe": // describe what this repl can do

                                BDictionary response = new BDictionary
                                {
                                    ["status"] = new BList(new String[] { "done" }),
                                    ["aux"] = new BList(),
                                    ["ops"] = new BDictionary
                                    {
                                        ["clone"] = new BList(),
                                        ["describe"] = new BList(),
                                        ["eval"] = new BList()
                                    }
                                };
                                Send(stream, ResponseFor(message, response));
                                break;
                            default:
                                logger.LogWarning("Unhandled Message");
                                break;
                        }
                    }
                    else
                    {
                        logger.LogWarning("Unhandled Message");
                    }

                }
            } catch (SocketException e)
            {
                logger.LogError(e.Message);
            } finally
            {
                server.Stop();
            }
        }
    }
}
