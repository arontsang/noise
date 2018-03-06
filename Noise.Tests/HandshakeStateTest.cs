using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Noise.Tests
{
	public class HandshakeStateTest
	{
		[Fact]
		public void TestProtocol()
		{
			var s = File.ReadAllText("Cacophony.txt");
			var json = JObject.Parse(s);

			var initBuffer = new byte[Constants.MaxMessageLength];
			var respBuffer = new byte[Constants.MaxMessageLength];

			foreach (var vector in json["vectors"])
			{
				var protocolName = GetString(vector, "protocol_name");
				var initPrologue = GetBytes(vector, "init_prologue");
				var initStatic = GetBytes(vector, "init_static");
				var initEphemeral = GetBytes(vector, "init_ephemeral");
				var initRemoteStatic = GetBytes(vector, "init_remote_static");
				var respPrologue = GetBytes(vector, "resp_prologue");
				var respStatic = GetBytes(vector, "resp_static");
				var respEphemeral = GetBytes(vector, "resp_ephemeral");
				var respRemoteStatic = GetBytes(vector, "resp_remote_static");
				var handshakeHash = GetBytes(vector, "handshake_hash");

				var initStaticPair = GetKeyPair(initStatic);
				var respStaticPair = GetKeyPair(respStatic);

				if (!Protocol.Create(protocolName, true, initPrologue, initStaticPair, initRemoteStatic, out var init))
				{
					continue;
				}

				if (!Protocol.Create(protocolName, false, respPrologue, respStaticPair, respRemoteStatic, out var resp))
				{
					continue;
				}

				var flags = BindingFlags.Instance | BindingFlags.NonPublic;
				var setDh = init.GetType().GetMethod("SetDh", flags);

				var initDh = new FixedKeyDh(initEphemeral);
				var respDh = new FixedKeyDh(respEphemeral);

				setDh.Invoke(init, new object[] { initDh });
				setDh.Invoke(resp, new object[] { respDh });

				ITransport respTransport = null;
				ITransport initTransport = null;

				foreach (var message in vector["messages"])
				{
					var payload = GetBytes(message, "payload");
					var ciphertext = GetBytes(message, "ciphertext");

					Span<byte> initMessage = null;
					Span<byte> respMessage = null;

					if (initTransport == null && respTransport == null)
					{
						initMessage = init.WriteMessage(payload, initBuffer, out initTransport);
						respMessage = resp.ReadMessage(initMessage, respBuffer, out respTransport);

						Swap(ref init, ref resp);
					}
					else
					{
						initMessage = initTransport.WriteMessage(payload, initBuffer);
						respMessage = respTransport.ReadMessage(initMessage, respBuffer);

						Swap(ref initTransport, ref respTransport);
					}

					Assert.Equal(ciphertext, initMessage.ToArray());
					Assert.Equal(payload, respMessage.ToArray());

					Swap(ref initBuffer, ref respBuffer);
				}

				init.Dispose();
				resp.Dispose();
			}
		}

		private static string GetString(JToken token, string property)
		{
			return (string)token[property] ?? String.Empty;
		}

		private static byte[] GetBytes(JToken token, string property)
		{
			return Hex.Decode(GetString(token, property));
		}

		private static KeyPair GetKeyPair(byte[] privateKey)
		{
			if (privateKey == null)
			{
				return null;
			}

			var publicKey = new byte[privateKey.Length];
			Libsodium.crypto_scalarmult_curve25519_base(publicKey, privateKey);

			return new KeyPair(privateKey, publicKey);
		}

		private static void Swap<T>(ref T x, ref T y)
		{
			var temp = x;
			x = y;
			y = temp;
		}
	}
}