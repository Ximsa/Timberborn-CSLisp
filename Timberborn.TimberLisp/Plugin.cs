using BencodeNET.Objects;
using Timberborn.ModManagerScene;
using CSLisp.Core;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using BencodeNET.Parsing;
using UnityEngine;

namespace Timberborn.TimberLisp
{
    public class Plugin : IModStarter
    {
        public void StartMod()
        {
            var server = new NreplServer();
            Thread t = new Thread(server.Initialize);
            t.IsBackground = true;
            t.Start();
        }
    }
    public class MyLogger : CSLisp.Core.ILogger
    {
        string name;
        public MyLogger(string name)
        {
            this.name = name;
        }
        bool CSLisp.Core.ILogger.EnableParsingLogging => false;

        bool CSLisp.Core.ILogger.EnableInstructionLogging => false;

        bool CSLisp.Core.ILogger.EnableStackLogging => false;

        /*void ILogger.Log(params object[] args)
        {
            var strings = args.Select(obj => (obj == null) ? "null" : obj.ToString());
            var message = string.Join(" ", strings);
            Console.WriteLine("["+name+"] "+message);
        }*/
        public void Log (params object[] args)
        {
            var strings = args.Select(obj => (obj == null) ? "null" : obj.ToString());
            var message = string.Join(" ", strings);
            Debug.Log("[" + name + "] " + message);
        }
    }

    public class NreplServer
    {
        readonly MyLogger logger;
        readonly Context lispContext;
        readonly ConcurrentQueue<BDictionary> messageQueue;
        NetworkStream stream;
        String sessionId;

        public NreplServer()
        {
            logger = new MyLogger("cs-repl");
            lispContext = new Context(logger: new MyLogger("cs-repl-code"));
            messageQueue = new ConcurrentQueue<BDictionary>();
            NREPLServerInstance.nreplServer = this;
        }
        private BDictionary ResponseFor(BDictionary oldMessage, BDictionary message) 
        {
            BString session = "none";
            BString id = "unknown";
            if (oldMessage.TryGetValue("session", out IBObject rawSession)) {
                session = (BString)rawSession;
            }

            if (oldMessage.TryGetValue("id", out IBObject rawId)) {
                id = rawId.ToString();
            }

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
                try {
                    value = String.Join(" ", lispContext.CompileAndExecute(code).Select(r => r.output));
                } catch (Exception e) { logger.Log(e.StackTrace); }
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
                {
                    rawMessage.Add(buffer[i]);
                }
            } while (stream.DataAvailable);
            return parser.Parse<BDictionary>(rawMessage.ToArray());
        }
        public void Initialize() {
            logger.Log("init");
            TcpListener server;
            BencodeParser parser = new BencodeParser();
            try {
                server = new TcpListener(IPAddress.Parse("127.0.0.1"), 1111);
                server.Start();
                logger.Log("starting....");
                var client = server.AcceptTcpClient();
                // connected
                logger.Log("connected!");
                stream = client.GetStream();
                while (true) {
                    BDictionary message = ReadMessage(stream, parser);
                    messageQueue.Enqueue(message);
                }
            } catch (Exception e) {
                logger.Log(e.Message);
            }
        }
        public void HandleMessages() {
            if(messageQueue is null) {
                return;
            }
            while (messageQueue.TryDequeue(out BDictionary message)) {
                if (message.TryGetValue("op", out IBObject value)) {
                    switch (((BString) value).ToString()) {
                        case "clone": // register a new session
                            sessionId = RegisterSession(stream, message);
                            break;
                        case "eval": // evaluate message
                            try {
                                EvalMsg(stream, message);
                            } catch (Exception e) {
                                logger.Log(e.Message);
                                SendException(stream, message, e);
                            }
                            break;
                        case "describe": // describe what this repl can do
                            BDictionary response = new BDictionary {
                                ["status"] = new BList(new String[] { "done" }),
                                ["aux"] = new BList(),
                                ["ops"] = new BDictionary {
                                    ["clone"] = new BList(),
                                    ["describe"] = new BList(),
                                    ["eval"] = new BList()
                                }
                            };
                            Send(stream, ResponseFor(message, response));
                            break;
                        default:
                            logger.Log("Unhandled Message");
                            break;
                    }
                } else {
                    logger.Log("Unhandled Message");
                }
            }
        }
    }
    public static class NREPLServerInstance
    {
        public static NreplServer nreplServer;
    }
}
