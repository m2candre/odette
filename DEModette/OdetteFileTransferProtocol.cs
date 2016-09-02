﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using TecWare.DE.Server;
using TecWare.DE.Stuff;

namespace TecWare.DE.Odette
{
	#region -- enum OdetteVersion -------------------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	internal enum OdetteVersion
	{
		Unknown,
		Rev12,
		Rev13,
		Rev14,
		Rev20
	} // enum OdetteVersion

	#endregion

	#region -- enum OdetteCapabilities --------------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	[Flags]
	public enum OdetteCapabilities
	{
		None = 0,
		Send = 1,
		Receive = 2,
		BufferCompression = 4,
		Restart = 8,
		SpecialLogic = 16,
		SecureAuthentification = 32
	} // enum OdetteCapabilities

	#endregion

	#region -- enum OdetteEndSessionReasonCode ------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	internal enum OdetteEndSessionReasonCode
	{
		/// <summary>Normal session termination.</summary>
		None = 0,
		/// <summary>An Exchange Buffer contains an invalid command code (1st octet of the buffer).</summary>
		CommandNotRecognised = 1,
		/// <summary>An Exchange Buffer contains an invalid command for the current state of the receiver.</summary>
		ProtocolViolation = 2,
		/// <summary>A Start Session (SSID) command contains an unknown or invalid Identification Code.</summary>
		UserCodeNotKnown = 3,
		/// <summary>A Start Session (SSID) command contained an invalid password.</summary>
		InvalidPassword = 4,
		/// <summary>The local site has entered an emergency close down mode. Communications are being forcibly terminated.</summary>
		LocalSiteEmergencyCloseDown = 5,
		/// <summary>A field within a Command Exchange Buffer contains invalid data.</summary>
		CommandContainedInvalidData = 6,
		/// <summary>The length of the Exchange Buffer as determined by the Stream Transmission Header differs from the length implied by the Command Code.</summary>
		ExchangeBufferSizeError = 7,
		/// <summary>The request for connection has been denied due to a resource shortage. The connection attempt should be retried later.</summary>
		ResourcesNotAvailable = 8,
		/// <summary></summary>
		TimeOut = 9,
		/// <summary></summary>
		ModeOrCapabilitiesIncompatible = 10,
		/// <summary></summary>
		InvalidChallengeResponse = 11,
		/// <summary></summary>
		SecureAuthenticationRequirementsIncompatible = 12,
		/// <summary>An error was detected for which no specific code is defined.</summary>
		UnspecifiedAbortCode = 99
	} // enum OdetteEndSessionReasonCode

	#endregion

	#region -- enum OdetteSecurityLevels ------------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	[Flags]
	internal enum OdetteSecurityLevels
	{
		None = 0,
		Encrypted = 1,
		Signed = 2,
		EncryptedAndSigned = Encrypted | Signed
	} // enum OdetteSecurityLevels

	#endregion

	#region -- enum OdetteAnswerReason --------------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	public enum OdetteAnswerReason
	{
		None = 0,
		InvalidFilename = 01,
		InvalidDestination = 02,
		InvalidOrigin = 03,
		StorageRecordFormatNotSupported = 04,
		MaximumRecordLengthNotSupported = 05,
		FileSizeIsTooBig = 06,
		InvalidRecordCount = 10,
		InvalidByteCount = 11,
		AccessMethodFailure = 12,
		DuplicateFile = 13,
		FileDirectionRefused = 14,
		CipherSuiteNotSupported = 15,
		EncryptedFileNotAllowed = 16,
		UnencryptedFileNotAllowed = 17,
		CompressionNotAllowed = 18,
		SignedFileNotAllowed = 19,
		UnsignedFileNotAllowed = 20,
		InvalidFileSignature = 21,
		FileDecryptionFailure = 22,
		FileDecompressionFailure = 23,
		UnspecifiedReason = 99
	} // enum OdetteAnswerReason

	#endregion

	#region -- class OdetteException ----------------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	// <summary></summary>
	internal class OdetteException : Exception
	{
		private readonly OdetteEndSessionReasonCode reasonCode;

		public OdetteException(OdetteEndSessionReasonCode reasonCode, string reasonText, Exception innerException = null)
			: base(reasonText, innerException)
		{
			this.reasonCode = reasonCode;
		} // ctor

		public OdetteEndSessionReasonCode ReasonCode => reasonCode;
	} // class OdetteException

	#endregion

	#region -- class OdetteRemoteEndException -------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary>Is thrown if the remote host, closes the connection.</summary>	
	internal class OdetteRemoteEndException : OdetteException
	{
		public OdetteRemoteEndException(OdetteEndSessionReasonCode reasonCode, string reasonText)
			: base(reasonCode, reasonText)
		{
		} // ctor
	} // class OdetteRemoteEndException

	#endregion

	#region -- class OdetteAbortException -----------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary>Is thrown if the session is finished normally.</summary>	
	internal class OdetteAbortException : OdetteException
	{
		public OdetteAbortException(OdetteEndSessionReasonCode reasonCode, string reasonText)
			: base(reasonCode, reasonText)
		{
		} // ctor
	} // class OdetteException

	#endregion

	#region -- class OdetteFileServiceException -----------------------------------------

	public class OdetteFileServiceException : Exception
	{
		private readonly OdetteAnswerReason reasonCode;
		private readonly string reasonText;
		private readonly bool retryFlag;

		public OdetteFileServiceException(OdetteAnswerReason reasonCode, string reasonText = null, bool retryFlag = false, Exception innerException = null)
			: base(reasonText ?? OdetteFtp.GetReasonText(reasonCode), innerException)
		{
			this.reasonCode = reasonCode;
			this.retryFlag = retryFlag;
			this.reasonText = reasonText;
		} // ctor

		public OdetteAnswerReason ReasonCode => reasonCode;
		public string ReasonText => reasonText;
		public bool RetryFlag => retryFlag;
	} // class OdetteFileServiceException

	#endregion

	#region -- class OdetteFtp ----------------------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	internal sealed class OdetteFtp
	{
		internal struct CipherSuit
		{
			public string Symetric;
			public string Asymetric;
			public string Hash;
		} // class CipherSuit

		public readonly CipherSuit[] cipherSuits = new CipherSuit[]
		{
			new CipherSuit() { Symetric = null, Asymetric = null, Hash = null },
			new CipherSuit() { Symetric = "3DES_EDE_CBC_3KEY", Asymetric = "RSA_PKCS1_15", Hash = "SHA1" },
			new CipherSuit() { Symetric = "AES_256_CBC", Asymetric = "RSA_PKCS1_15", Hash = "SHA1" },
			new CipherSuit() { Symetric = "3DES_EDE_CBC_3KEY", Asymetric = "RSA_PKCS1_15", Hash = "SHA256" },
			new CipherSuit() { Symetric = "AES_256_CBC", Asymetric = "RSA_PKCS1_15", Hash = "SHA256" },
			new CipherSuit() { Symetric = "3DES_EDE_CBC_3KEY", Asymetric = "RSA_PKCS1_15", Hash = "SHA512" },
			new CipherSuit() { Symetric = "AES_256_CBC", Asymetric = "RSA_PKCS1_15", Hash = "SHA512" }
		};

		#region -- class OdetteCommand ----------------------------------------------------

		///////////////////////////////////////////////////////////////////////////////
		/// <summary>Gives a command buffer a structure.</summary>
		private abstract class OdetteCommand
		{
			private readonly byte[] buffer;
			private int length;

			#region -- Ctor/Dtor --------------------------------------------------------------

			protected OdetteCommand(byte[] buffer, int length)
			{
				this.buffer = buffer;
				this.length = length;
			} // ctor

			internal string DebugString()
			{
				var sb = new StringBuilder();

				sb.Append(Signature)
					.Append('{');

				var c = false;
				foreach (var p in GetType().GetRuntimeProperties())
				{
					if (p.CanRead && p.CanWrite)
					{
						if (c)
							sb.Append(", ");
						else
							c = true;

						sb.Append(p.Name)
							.Append(" = ");

						var obj = p.GetValue(this);
						if (obj == null)
							sb.Append("null");
						else if (obj is string)
							sb.Append('"').Append(((string)obj).Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t").Replace("\r", "\\r")).Append('"');
						else if (obj is byte[])
						{
							var b = (byte[])obj;
							if (b.Length == 0)
								sb.Append("null");
							else
							{
								sb.Append("L:").Append(b.Length).Append(':');
								var m = Math.Min(64, b.Length);
								for (var i = 0; i < m; i++)
									sb.Append(b[i].ToString("X2")).Append(' ');
							}
						}
						else if (obj is DateTime)
							sb.Append(((DateTime)obj).ToString("yyyy-MM-dd HH:mm:ss,ffff"));
						else
							sb.Append(obj);
					}
				}
				sb.Append('}');
				return sb.ToString();
			} // func DebugString

			public override string ToString()
				=> $"{GetType().Name}[len={Length}; {Signature}]";

			#endregion

			#region -- Read primitives --------------------------------------------------------

			protected string ReadAscii(int offset, int length)
				=> Encoding.ASCII.GetString(buffer, offset, length).TrimEnd(' ');

			protected string ReadUtf8(int offset)
				=> Encoding.UTF8.GetString(buffer, offset + 3, ReadNumber(offset, 3)).TrimEnd(' ');

			protected int ReadNumber(int offset, int length)
				=> Int32.Parse(ReadAscii(offset, length));

			protected long ReadNumberLong(int offset, int length)
				=> Int64.Parse(ReadAscii(offset, length));

			protected uint ReadUnsignedNumber(int offset, int length)
			{
				if (length == 0)
					return 0;
				else if (length == 1)
					return buffer[offset];
				else if (length == 2)
					return (uint)buffer[offset + 1] | ((uint)buffer[offset] << 8);
				else if (length == 3)
					return (uint)buffer[offset + 2] | ((uint)buffer[offset + 1] << 8) | ((uint)buffer[offset] << 16);
				else
					return (uint)buffer[offset + 3] | ((uint)buffer[offset + 2] << 8) | ((uint)buffer[offset + 1] << 16) | ((uint)buffer[offset] << 24);
			} // func ReadUnsignedNumber

			protected byte[] ReadBytes(int offset)
			{
				var l = ReadUnsignedNumber(offset, 2);
				if (l == 0)
					return null;
				else
				{
					var r = new byte[l];
					Array.Copy(buffer, offset + 2, r, 0, l);
					return r;
				}
			} // func ReadBytes

			protected bool IsValidFixed(int offset, string expected)
			{
				var result = ReadAscii(offset, expected.Length);
				return result == expected;
			} // proc TestFixed

			protected bool IsValidFixed(int offset, params string[] args)
			{
				if (args == null || args.Length == 0)
					throw new ArgumentNullException("args");

				var result = ReadAscii(offset, args[0].Length);
				var index = Array.IndexOf(args, result);
				return index >= 0;
			} // proc TestFixed

			#endregion

			#region -- Write primitives -------------------------------------------------------

			private byte GetLetter(char c)
			{
				var b = (int)c;
				if (b == (int)' ' || b == (int)'/' || b == (int)'-' || b == (int)'.' || b == (int)'&' || b == (int)'(' || b == (int)')' ||
						b >= 0x30 && b <= 0x39 || // 0..9
						b >= 0x41 && b <= 0x5A)// A..Z
					return unchecked((byte)b);
				else if (b >= 0x61 && b <= 0x7A) // a..z
					return unchecked((byte)(b - 0x20));
				else
					throw new OdetteFileServiceException(OdetteAnswerReason.InvalidFilename, "Ascii field contains a invalid char.");
			} // func GetLetter

			protected void WriteAscii(int offset, int count, string data)
			{
				var endAt = offset + count;

				// encode string
				for (var i = 0; i < data.Length && offset < endAt; i++)
					buffer[offset++] = GetLetter(data[i]);

				// fill with spaces
				while (offset < endAt)
					buffer[offset++] = (byte)' ';
			} // proc WriteAscii

			protected void WriteNumber(int offset, int length, long value)
			{
				var currentIndex = offset + length - 1;
				while (currentIndex >= offset && value > 0)
				{
					var r = value % 10;
					value = value / 10;
					buffer[currentIndex--] = unchecked((byte)('0' + r));
				}
				if (value != 0)
					throw new OverflowException(String.Format("Value {0} is longer than {1} digits.", value, length));

				while (currentIndex >= offset)
					buffer[currentIndex--] = (byte)'0';
			} // proc WriteNumber

			protected void WriteUnsignedNumber(int offset, int length, int value)
			{
				for (var i = length - 1; i >= 0; i--)
				{
					buffer[offset + i] = unchecked((byte)value);
					value = value >> 8;
				}

				if (value != 0)
					throw new ArgumentOutOfRangeException("Value is too large.");
			} // proc WriteUnsignedNumber

			protected int WriteUtf8(int offset, string text)
			{
				if (String.IsNullOrEmpty(text))
				{
					WriteNumber(offset, 3, 0);
					offset += 3;
				}
				else
					offset = WriteUtf8(offset, Encoding.UTF8.GetBytes(text));

				return offset;
			} // prop WriteUtf8

			private int WriteUtf8(int offset, byte[] text)
			{
				WriteNumber(offset, 3, text.Length);
				offset += 3;
				text.CopyTo(Data, offset);
				offset += text.Length;
				return offset;
			} // prop WriteUtf8

			protected void WriteUtf8WithResize(int offset, string text)
			{
				var bytes = Encoding.UTF8.GetBytes(text);

				// move data
				var sourceIndex = offset + ReadNumber(offset, 3) + 3;
				var targetIndex = offset + bytes.Length + 3;
				if (sourceIndex != targetIndex)
				{
					Array.Copy(Data, sourceIndex, Data, targetIndex, Length - sourceIndex);
					Length = Length + targetIndex - sourceIndex;
				}

				if (bytes.Length == 0)
					WriteNumber(offset, 3, 0);
				else
					WriteUtf8(offset, bytes);
			} // proc WriteUtf8WithResize

			protected int WriteBytes(int offset, byte[] bytes)
			{
				if (bytes == null || bytes.Length == 0)
				{
					WriteUnsignedNumber(offset, 2, bytes.Length);
					offset += 2;
				}
				else
				{
					WriteUnsignedNumber(offset, 2, bytes.Length);
					offset += 2;
					bytes.CopyTo(Data, offset);
					offset += bytes.Length;
				}
				return offset;
			} // WriteBytes

			protected void WriteBytesWithResize(int offset, byte[] value)
			{
				// move data
				var sourceIndex = offset + unchecked((int)ReadUnsignedNumber(offset, 2)) + 2;
				var targetIndex = offset + (value?.Length ?? 0) + 2;
				if (sourceIndex != targetIndex)
				{
					Array.Copy(Data, sourceIndex, Data, targetIndex, Length - sourceIndex);
					Length = Length + targetIndex - sourceIndex;
				}

				WriteBytes(offset, value);
			} // proc WriteBytesWithResize

			#endregion

			#region -- Validate ---------------------------------------------------------------

			public OdetteCommand CheckValid(bool outbound)
			{
				string error;
				if (!IsValid(out error))
				{
					if (outbound)
						throw new ArgumentException(String.Format("Invalid command in out buffer ({0}, error: {1}).", GetType().Name, error));
					else
						throw new OdetteException(OdetteEndSessionReasonCode.CommandContainedInvalidData, error);
				}
				return this;
			} // proc CheckValid

			public virtual bool IsValid(out string error)
			{
				if (Signature != (char)buffer[0])
				{
					error = String.Format("Signature of the command is wrong (expected: {0}, found: {1})", Signature, (char)buffer[0]);
					return false;
				}
				error = null;
				return true;
			} // func IsValid

			#endregion

			public abstract char Signature { get; }
			public byte[] Data => buffer;
			public int Length { get { return length; } protected set { length = value; } }
		} // class OdetteTransmissionBuffer

		#endregion

		#region -- class StartSessionReadyMessageCommand ----------------------------------

		private sealed class StartSessionReadyMessageCommand : OdetteCommand
		{
			private const string ReadyMessage = "ODETTE FTP READY";

			public StartSessionReadyMessageCommand(byte[] transmissionBuffer)
				: base(transmissionBuffer, 19)
			{
				Data[0] = (byte)Signature;
				WriteAscii(1, 17, ReadyMessage);
				Data[18] = 0x0D;
			} // ctor

			public StartSessionReadyMessageCommand(byte[] transmissionBuffer, int length)
				: base(transmissionBuffer, length)
			{
			} // ctor

			public override bool IsValid(out string error)
			{
				if (!IsValidFixed(1, ReadyMessage))
				{
					error = "Start Session Ready Message is invalid.";
					return false;
				}
				else if (!IsValidFixed(18, "\x0D", "\x8D"))
				{
					error = "End of command is not valid.";
					return false;
				}
				else
					return base.IsValid(out error);
			} // func IsValid

			public sealed override char Signature => 'I';
			public string RM { get { return ReadAscii(1, 17); } set { } } // dummy for a nice debug message
		} // class StartSessionReadyMessageCommand

		#endregion

		#region -- class StartSessionCommand ----------------------------------------------

		private sealed class StartSessionCommand : OdetteCommand
		{
			public StartSessionCommand(byte[] transmissionBuffer)
				: base(transmissionBuffer, 61)
			{
				Data[0] = (byte)Signature; // SSIDCMD
				Reserved = String.Empty;
				Data[60] = 0x0D;
			} // ctor

			public StartSessionCommand(byte[] transmissionBuffer, int length)
				: base(transmissionBuffer, length)
			{
			} // ctor

			public override bool IsValid(out string error)
			{
				if (Length != 61)
				{
					error = String.Format("Command length is invalid (Expected: {0}, found: {1})", 61, Length);
					return false;
				}
				else if (!IsValidFixed(60, "\x0D", "\x8D"))
				{
					error = "End of command is not valid.";
					return false;
				}
				else
					return base.IsValid(out error);
			} // func IsValue

			public OdetteVersion Version // SSIDLEV
			{
				get
				{
					var v = ReadNumber(1, 1);
					switch (v)
					{
						case 1:
							return OdetteVersion.Rev12;
						case 2:
							return OdetteVersion.Rev13;
						case 4:
							return OdetteVersion.Rev14;
						case 5:
							return OdetteVersion.Rev20;
						default:
							return OdetteVersion.Unknown;
					}
				}
				set
				{
					switch (value)
					{
						case OdetteVersion.Rev12:
							WriteNumber(1, 1, 1);
							break;
						case OdetteVersion.Rev13:
							WriteNumber(1, 1, 2);
							break;
						case OdetteVersion.Rev14:
							WriteNumber(1, 1, 4);
							break;
						case OdetteVersion.Rev20:
							WriteNumber(1, 1, 5);
							break;
						default:
							throw new ArgumentException("version is unknown");
					}
				}
			} // prop Version

			public string InitiatorCode // SSIDCODE
			{
				get { return ReadAscii(2, 25); }
				set { WriteAscii(2, 25, value); }
			} // prop InitiatorCode

			public string Password // SSIDPSWD
			{
				get { return ReadAscii(27, 8); }
				set { WriteAscii(27, 8, value); }
			} // prop Password

			public int DataExchangeBuffer // SSIDSDEB
			{
				get { return ReadNumber(35, 5); }
				set { WriteNumber(35, 5, value); }
			} // prop DataExchangeBuffer

			public int BufferCreditSize // SSIDCRED 
			{
				get { return ReadNumber(44, 3); }
				set { WriteNumber(44, 3, value); }
			} // prop BufferCreditSize

			public string UserData // SSIDUSER
			{
				get { return ReadAscii(52, 8); }
				set { WriteAscii(52, 8, value); }
			} // prop UserData

			public string Reserved // SSIDRSV1
			{
				get { return ReadAscii(48, 4); }
				set { WriteAscii(48, 4, value); }
			} // prop ReservedV1

			public OdetteCapabilities Capabilities
			{
				get
				{
					var caps = OdetteCapabilities.None;

					switch (ReadAscii(40, 1)[0]) // SSIDSR
					{
						case 'R':
							caps |= OdetteCapabilities.Receive;
							break;
						case 'S':
							caps |= OdetteCapabilities.Send;
							break;
						case 'B':
							caps |= OdetteCapabilities.Receive;
							caps |= OdetteCapabilities.Send;
							break;
						default:
							throw new OdetteAbortException(OdetteEndSessionReasonCode.CommandContainedInvalidData, "SSIDSR must be R,S or B.");
					}

					if (ReadAscii(41, 1) == "Y") // SSIDCMPR
						caps |= OdetteCapabilities.BufferCompression;
					if (ReadAscii(42, 1) == "Y") // SSIDREST
						caps |= OdetteCapabilities.Restart;
					if (ReadAscii(43, 1) == "Y") // SSIDSPEC
						caps |= OdetteCapabilities.SpecialLogic;

					if (ReadAscii(47, 1) == "Y")  // SSIDAUTH
						caps |= OdetteCapabilities.SecureAuthentification;

					return caps;
				}
				set
				{
					if ((value & (OdetteCapabilities.Send | OdetteCapabilities.Receive)) == (OdetteCapabilities.Send | OdetteCapabilities.Receive))
						WriteAscii(40, 1, "B");
					else if ((value & OdetteCapabilities.Send) != 0)
						WriteAscii(40, 1, "S");
					else if ((value & OdetteCapabilities.Receive) != 0)
						WriteAscii(40, 1, "R");
					else
						throw new OdetteAbortException(OdetteEndSessionReasonCode.CommandContainedInvalidData, "Send, Receive or Both must be set in SSIDSR.");

					WriteAscii(41, 1, (value & OdetteCapabilities.BufferCompression) != 0 ? "Y" : "N"); // SSIDCMPR
					WriteAscii(42, 1, (value & OdetteCapabilities.Restart) != 0 ? "Y" : "N"); // SSIDREST
					WriteAscii(43, 1, (value & OdetteCapabilities.SpecialLogic) != 0 ? "Y" : "N");  // SSIDSPEC
					WriteAscii(47, 1, (value & OdetteCapabilities.SecureAuthentification) != 0 ? "Y" : "N"); // SSIDAUTH
				}
			} // prop Capabitlities

			public sealed override char Signature => 'X';
		} // class StartSessionCommand

		#endregion

		#region -- class StartFileCommand -------------------------------------------------

		private abstract class StartFileCommand : OdetteCommand, IOdetteFileDescription
		{
			public StartFileCommand(byte[] transmissionBuffer)
				: base(transmissionBuffer, 1)
			{
				Data[0] = (byte)Signature;
				Reserved = String.Empty;
			} // ctor

			public StartFileCommand(byte[] transmissionBuffer, int length)
				: base(transmissionBuffer, length)
			{
			} // ctor

			public string VirtualFileName
			{
				get { return ReadAscii(1, 26); }
				set { WriteAscii(1, 26, value); }
			} // prop VirtualFileName

			public string UserData
			{
				get { return ReadAscii(48, 8); }
				set { WriteAscii(48, 8, value); }
			} // prop VirtualFileDatasetName

			public string Destination
			{
				get { return ReadAscii(56, 25); }
				set { WriteAscii(56, 25, value); }
			} // prop VirtualFileDatasetName

			public string Originator
			{
				get { return ReadAscii(81, 25); }
				set { WriteAscii(81, 25, value); }
			} // prop VirtualFileDatasetName

			public OdetteFileFormat Format
			{
				get
				{
					switch (ReadAscii(106, 1)[0])
					{
						case 'F':
							return OdetteFileFormat.Fixed;
						case 'V':
							return OdetteFileFormat.Variable;
						case 'T':
							return OdetteFileFormat.Text;
						default:
							return OdetteFileFormat.Unstructured;
					}
				}
				set
				{
					switch (value)
					{
						case OdetteFileFormat.Fixed:
							WriteAscii(106, 1, "F");
							break;
						case OdetteFileFormat.Variable:
							WriteAscii(106, 1, "V");
							break;
						case OdetteFileFormat.Text:
							WriteAscii(106, 1, "T");
							MaximumRecordSize = 0;
							break;
						default:
							WriteAscii(106, 1, "U");
							MaximumRecordSize = 0;
							break;
					}
				}
			} // prop Format

			public int MaximumRecordSize
			{
				get { return ReadNumber(107, 5); }
				set { WriteNumber(107, 5, value); }
			} // prop MaximumRecordSize

			public abstract string Reserved { get; set; }

			public abstract DateTime FileStamp { get; set; }
			public abstract long FileSize { get; set; }
			public abstract long RestartPosition { get; set; }

			public abstract string Description { get; set; }
			public abstract long FileSizeUnpacked { get; set; }

			public sealed override char Signature => 'H';
		} // class StartFileCommand

		#endregion

		#region -- class StartFileCommandV1 -----------------------------------------------

		private sealed class StartFileCommandV1 : StartFileCommand
		{
			public StartFileCommandV1(byte[] transmissionBuffer)
				: base(transmissionBuffer)
			{
				Length = 128;
			} // ctor

			public StartFileCommandV1(byte[] transmissionBuffer, int length)
				: base(transmissionBuffer, length)
			{
			} // ctor

			public override string Reserved
			{
				get { return ReadAscii(27, 9); }
				set { WriteAscii(27, 9, value); }
			} // prop Reserved

			public override DateTime FileStamp
			{
				get { return DateTime.ParseExact(ReadAscii(36, 12), "yyMMddHHmmss", CultureInfo.InvariantCulture); }
				set { WriteAscii(36, 12, value.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture)); }
			} // prop Stamp

			public override long FileSize
			{
				get { return ReadNumber(112, 7); }
				set { WriteNumber(112, 7, value); }
			} // prop FileSize

			public override long RestartPosition
			{
				get { return ReadNumber(119, 9); }
				set { WriteNumber(119, 9, value); }
			} // prop FileSize

			public override string Description { get { return String.Empty; } set { throw new NotImplementedException(); } }
			public override long FileSizeUnpacked { get { return 0; } set { throw new NotImplementedException(); } }
		} // class StartFileCommandV1

		#endregion

		#region -- class StartFileCommandV2 -----------------------------------------------

		private sealed class StartFileCommandV2 : StartFileCommand
		{
			public StartFileCommandV2(byte[] transmissionBuffer)
				: base(transmissionBuffer)
			{
				Length = 165;
			} // ctor

			public StartFileCommandV2(byte[] transmissionBuffer, int length)
				: base(transmissionBuffer, length)
			{
			} // ctor

			public override string Reserved
			{
				get { return ReadAscii(27, 3); }
				set { WriteAscii(27, 3, value); }
			} // prop Reserved

			public override DateTime FileStamp
			{
				get { return DateTime.ParseExact(ReadAscii(30, 18), "yyyyMMddHHmmssffff", CultureInfo.InvariantCulture); }
				set { WriteAscii(30, 18, value.ToString("yyyyMMddHHmmssffff", CultureInfo.InvariantCulture)); }
			} // prop Stamp

			public override long FileSize
			{
				get { return ReadNumber(112, 13); }
				set { WriteNumber(112, 13, value); }
			} // prop FileSize

			public override long FileSizeUnpacked
			{
				get { return ReadNumber(125, 13); }
				set { WriteNumber(125, 13, value); }
			} // prop FileSizeOriginal

			public override long RestartPosition
			{
				get { return ReadNumber(138, 17); }
				set { WriteNumber(138, 17, value); }
			} // prop FileSize

			public OdetteSecurityLevels SecurityLevel
			{
				get { return (OdetteSecurityLevels)ReadNumber(155, 2); }
				set { WriteNumber(155, 2, (int)value); }
			} // prop SecurityLevel

			public int CipherSuite
			{
				get { return ReadNumber(157, 2); }
				set { WriteNumber(157, 2, value); }
			} // prop CipherSuite

			public int Compressed { get { return ReadNumber(159, 1); } set { WriteNumber(159, 1, value); } }
			public int Enveloped { get { return ReadNumber(160, 1); } set { WriteNumber(160, 1, value); } }
			public bool EerpSigned { get { return ReadAscii(161, 1) == "Y"; } set { WriteAscii(161, 1, value ? "Y" : "N"); } }

			public override string Description
			{
				get { return ReadUtf8(162); }
				set { Length = WriteUtf8(162, value); }
			} // prop Description
		} // class StartFileCommand

		#endregion

		#region -- class StartFilePositiveAnswerCommand -----------------------------------

		private abstract class StartFilePositiveAnswerCommand : OdetteCommand
		{
			public StartFilePositiveAnswerCommand(byte[] transmissionBuffer, int length)
				: base(transmissionBuffer, length)
			{
			} // ctor

			public abstract long RestartPosition { get; set; }
			public sealed override char Signature => '2';
		} // class StartFilePositiveAnswerCommand

		#endregion

		#region -- class StartFilePositiveAnswerCommandV1 ---------------------------------

		private sealed class StartFilePositiveAnswerCommandV1 : StartFilePositiveAnswerCommand
		{
			public StartFilePositiveAnswerCommandV1(byte[] transmissionBuffer)
				: base(transmissionBuffer, 10)
			{
				Data[0] = (byte)Signature;
			} // ctor

			public StartFilePositiveAnswerCommandV1(byte[] transmissionBuffer, int length)
				: base(transmissionBuffer, length)
			{
			} // ctor

			public override long RestartPosition
			{
				get { return ReadNumberLong(1, 9); }
				set { WriteNumber(1, 9, value); }
			} // prop RestartPosition
		} // class StartFilePositiveAnswerCommandV1

		#endregion

		#region -- class StartFilePositiveAnswerCommandV2 ---------------------------------

		private sealed class StartFilePositiveAnswerCommandV2 : StartFilePositiveAnswerCommand
		{
			public StartFilePositiveAnswerCommandV2(byte[] transmissionBuffer)
				: base(transmissionBuffer, 18)
			{
				Data[0] = (byte)Signature;
			} // ctor

			public StartFilePositiveAnswerCommandV2(byte[] transmissionBuffer, int length)
				: base(transmissionBuffer, length)
			{
			} // ctor

			public override long RestartPosition
			{
				get { return ReadNumberLong(1, 17); }
				set { WriteNumber(1, 17, value); }
			} // prop RestartPosition
		} // class StartFilePositiveAnswerCommandV2

		#endregion

		#region -- class StartFileNegativeAnswerCommand -----------------------------------

		private abstract class StartFileNegativeAnswerCommand : OdetteCommand
		{
			public StartFileNegativeAnswerCommand(byte[] transmissionBuffer)
				: base(transmissionBuffer, 4)
			{
				Data[0] = (byte)Signature;
			} // ctor

			public StartFileNegativeAnswerCommand(byte[] transmissionBuffer, int length)
				: base(transmissionBuffer, length)
			{
			} // ctor

			public OdetteAnswerReason AnswerReason
			{
				get { return (OdetteAnswerReason)ReadNumber(1, 2); }
				set { WriteNumber(1, 2, (int)value); }
			} // prop AnswerReason

			public bool RetryFlag
			{
				get { return ReadAscii(3, 1) == "Y"; }
				set { WriteAscii(3, 1, value ? "Y" : "N"); }
			} // prop RetryFlag

			public abstract string ReasonText { get; set; }

			public sealed override char Signature => '3';
		} // class StartFileNegativeAnswerCommand

		#endregion

		#region -- class StartFileNegativeAnswerCommandV1 ---------------------------------

		private sealed class StartFileNegativeAnswerCommandV1 : StartFileNegativeAnswerCommand
		{
			public StartFileNegativeAnswerCommandV1(byte[] transmissionBuffer)
				: base(transmissionBuffer)
			{
			} // ctor

			public StartFileNegativeAnswerCommandV1(byte[] transmissionBuffer, int length)
				: base(transmissionBuffer, length)
			{
			} // ctor

			public override string ReasonText
			{
				get { return GetReasonText(AnswerReason); }
				set { }
			} // prop ReasonText
		} // class StartFileNegativeAnswerCommandV1

		#endregion

		#region -- class StartFileNegativeAnswerCommandV2 ---------------------------------

		private sealed class StartFileNegativeAnswerCommandV2 : StartFileNegativeAnswerCommand
		{
			public StartFileNegativeAnswerCommandV2(byte[] transmissionBuffer)
				: base(transmissionBuffer)
			{
				WriteNumber(4, 3, 0);
				Length = 7;
			} // ctor

			public StartFileNegativeAnswerCommandV2(byte[] transmissionBuffer, int length)
				: base(transmissionBuffer, length)
			{
			} // ctor

			public override string ReasonText
			{
				get
				{
					var v = ReadUtf8(4);
					return String.IsNullOrEmpty(v) ? GetReasonText(AnswerReason) : v;
				}
				set { Length = WriteUtf8(4, value); }
			} // prop ReasonText
		} // class StartFileNegativeAnswerCommandV2

		#endregion

		#region -- class DataCommand ------------------------------------------------------

		private const byte EOF = 0x80;
		private const byte CF = 0x40;
		private const byte LENMASK = 0x3F;

		private sealed class DataCommand : OdetteCommand
		{
			public DataCommand(byte[] transmissionBuffer)
				: base(transmissionBuffer, 1)
			{
				Data[0] = (byte)Signature;
			} // ctor

			public DataCommand(byte[] transmissionBuffer, int length)
				: base(transmissionBuffer, length)
			{
			} // ctor

			public bool WriteToStream(IOdetteFileWriter destination)
			{
				return WriteToStreamInternal(destination, Data, Length);
			} // func Write

			public bool FillFromStream(IOdetteFileReader source, bool allowCompressing = false)
			{
				int dataLength;
				var endOfStream = FillFromStreamInternal(source, allowCompressing, Data, out dataLength);
				Length = dataLength;
				return endOfStream;
			}  // func FillFromStream

			public sealed override char Signature => 'D';
		} // class DataCommand

		private const int NumberOfRepeatingCharacter = 4;

		private static int FillFromStreamCountRepeats(byte[] b, int offset, int count, bool collectRepeats)
		{
			var tmpCount = offset + count;
			var a = b[offset++];
			var r = 1;
			while (offset < tmpCount)
			{
				if (b[offset] == a)
				{
					r++;
					if (!collectRepeats && r > NumberOfRepeatingCharacter)
						break;
				}
				else
					break;
				offset++;
			}
			return r;
		} // func FillFromStreamCountRepeats

		internal static bool WriteToStreamInternal(IOdetteFileWriter dst, byte[] data, int dataLength)
		{
			var repeatBuffer = new byte[63];
			var currentOffset = 1;
			var isEoR = false;

			while (currentOffset < dataLength)
			{
				var header = data[currentOffset++]; // sub record header

				// test for eor
				isEoR = (header & EOF) != 0;

				// test for compression
				var len = header & LENMASK; // get the length or counter
				if ((header & CF) != 0) // write the same byte n-times
				{
					var d = data[currentOffset++]; // data
					for (var i = 0; i < len; i++)
						repeatBuffer[i] = d;
					dst.Write(repeatBuffer, 0, len, isEoR);
				}
				else // normal octets
				{
					dst.Write(data, currentOffset, len, isEoR);
					currentOffset += len;
				}
			}

			return isEoR;
		} // func WriteToStreamInternal

		internal static bool FillFromStreamInternal(IOdetteFileReader src, bool allowCompressing, byte[] data, out int dataLength)
		{
			var currentOffset = 1; // is the current position in the data exchange buffer
			var maxBufferLength = data.Length; // is the maximum buffer size

			var subRecordBuffer = new byte[63];
			var subRecordBufferLength = 0;

			bool isEoR = false;
			var endOfStream = false;

			// loop until max buffer is filled
			while (!endOfStream && currentOffset < maxBufferLength)
			{
				// refill sub record
				var maxSubRecordLength = 63 - subRecordBufferLength;
				var readLength = maxBufferLength - currentOffset - 1 - subRecordBufferLength; // 1 is the size of the sub record header
				if (readLength > maxSubRecordLength)
					readLength = maxSubRecordLength;

				// read sub record buffer
				var lastWasEor = isEoR;
				if (readLength > 0) // can -1 one, but than it should be a repeatation
				{
					var readed = src.Read(subRecordBuffer, subRecordBufferLength, readLength, out isEoR);
					if (readed > 0) // beware of -1 one
						subRecordBufferLength = subRecordBufferLength + readed;
				}

				if (subRecordBufferLength <= 0) // end of stream
				{
					if (!lastWasEor)
					{
						if (isEoR)
							data[currentOffset++] = EOF; // write a eof/eor
						else
							throw new OdetteFileServiceException(OdetteAnswerReason.InvalidByteCount, "End of stream without End of record detected.");
					}
					endOfStream = true;
				}
				else if (allowCompressing) // compressed sub records allowed
				{
					#region -- compression --

					var testOffset = 0; // test position
					var subRecordBufferOffset = 0;

					do  // while is EoR
					{
						var repeats = 0; // number of repeats
						var collectRepeats = true;
						while (testOffset < subRecordBufferLength)
						{
							repeats = FillFromStreamCountRepeats(subRecordBuffer, testOffset, subRecordBufferLength - testOffset, collectRepeats);
							if (repeats > NumberOfRepeatingCharacter)
							{
								if (collectRepeats) // use repeats
								{
									testOffset += repeats;
									break;
								}
								else
									break; // write part
							}
							else // collect unrepeating stuff
							{
								collectRepeats = false;
								testOffset += repeats;
							}
						} // while testOffset < readed

						// check for real eor
						var isEoRandEnfOfBuffer = isEoR && testOffset >= subRecordBufferLength;

						// number of bytes
						var lengthOfSubRecord = testOffset - subRecordBufferOffset;
						Debug.Assert(lengthOfSubRecord < 64);

						if (collectRepeats) // write a repeat sub record
						{
							data[currentOffset++] = unchecked((byte)(lengthOfSubRecord | CF | (isEoRandEnfOfBuffer ? EOF : 0)));
							data[currentOffset++] = subRecordBuffer[subRecordBufferOffset];
						}
						else // write a data sub record
						{
							data[currentOffset++] = unchecked((byte)(lengthOfSubRecord | (isEoRandEnfOfBuffer ? EOF : 0)));
							Array.Copy(subRecordBuffer, subRecordBufferOffset, data, currentOffset, lengthOfSubRecord);
							currentOffset += lengthOfSubRecord;
						}

						// mark bytes as done
						subRecordBufferOffset = testOffset;

						if (isEoRandEnfOfBuffer)
							break;
					} while (isEoR);

					if (subRecordBufferOffset < subRecordBufferLength)
					{
						subRecordBufferLength -= subRecordBufferOffset;
						Array.Copy(subRecordBuffer, subRecordBufferOffset, subRecordBuffer, 0, subRecordBufferLength);
					}
					else
						subRecordBufferLength = 0;

					#endregion
				}
				else // normal sub record
				{
					data[currentOffset++] = unchecked((byte)(subRecordBufferLength | (isEoR ? EOF : 0)));
					Array.Copy(subRecordBuffer, 0, data, currentOffset, subRecordBufferLength);
					currentOffset += subRecordBufferLength;
					subRecordBufferLength = 0;
				}
			}

			dataLength = currentOffset;
			return endOfStream; // end of stream reached	
		} // proc FillFromStreamInternal

		#endregion

		#region -- class SetCreditCommand -------------------------------------------------

		private sealed class SetCreditCommand : OdetteCommand
		{
			public SetCreditCommand(byte[] transmissionBuffer)
				: base(transmissionBuffer, 3)
			{
				Data[0] = (byte)Signature;
				WriteAscii(1, 2, String.Empty);
			} // ctor

			public SetCreditCommand(byte[] transmissionBuffer, int length)
				: base(transmissionBuffer, length)
			{
			} // ctor

			public sealed override char Signature => 'C';
		} // class SetCredidCommand

		#endregion

		#region -- class EndFileCommand ---------------------------------------------------

		private abstract class EndFileCommand : OdetteCommand
		{
			public EndFileCommand(byte[] transmissionBuffer, int length)
				: base(transmissionBuffer, length)
			{
			} // ctor

			public abstract long RecordCount { get; set; }
			public abstract long UnitCount { get; set; }

			public sealed override char Signature => 'T';
		} // class EndFileCommand

		#endregion

		#region -- class EndFileCommandV1 -------------------------------------------------

		private sealed class EndFileCommandV1 : EndFileCommand
		{
			public EndFileCommandV1(byte[] transmissionBuffer)
				: base(transmissionBuffer, 22)
			{
				Data[0] = (byte)Signature;
			} // ctor

			public EndFileCommandV1(byte[] transmissionBuffer, int length)
				: base(transmissionBuffer, length)
			{
			} // ctor

			public override long RecordCount
			{
				get { return ReadNumberLong(1, 9); }
				set { WriteNumber(1, 9, value); }
			} // prop RecordCount

			public override long UnitCount
			{
				get { return ReadNumberLong(10, 12); }
				set { WriteNumber(10, 12, value); }
			} // prop UnitCount
		} // class EndFileCommandV1

		#endregion

		#region -- class EndFileCommandV2 -------------------------------------------------

		private sealed class EndFileCommandV2 : EndFileCommand
		{
			public EndFileCommandV2(byte[] transmissionBuffer)
				: base(transmissionBuffer, 35)
			{
				Data[0] = (byte)Signature;
			} // ctor

			public EndFileCommandV2(byte[] transmissionBuffer, int length)
				: base(transmissionBuffer, length)
			{
			} // ctor

			public override long RecordCount
			{
				get { return ReadNumberLong(1, 17); }
				set { WriteNumber(1, 17, value); }
			} // prop RecordCount

			public override long UnitCount
			{
				get { return ReadNumberLong(18, 17); }
				set { WriteNumber(18, 17, value); }
			} // prop UnitCount
		} // class EndFileCommandV2

		#endregion

		#region -- class EndFilePositiveAnswerCommand -------------------------------------

		private sealed class EndFilePositiveAnswerCommand : OdetteCommand
		{
			public EndFilePositiveAnswerCommand(byte[] transmissionBuffer)
				: base(transmissionBuffer, 2)
			{
				Data[0] = (byte)Signature;
				Data[1] = (byte)'N';
			} // ctor

			public EndFilePositiveAnswerCommand(byte[] transmissionBuffer, int length)
				: base(transmissionBuffer, length)
			{
			} // ctor

			public bool ChangeDirection
			{
				get { return ReadAscii(1, 1) == "Y"; }
				set { WriteAscii(1, 1, value ? "Y" : "N"); }
			} // prop ChangeDirection

			public override char Signature => '4';
		} // class EndFilePositiveAnswerCommand

		#endregion

		#region -- class EndFileNegativeAnswerCommand -------------------------------------

		private abstract class EndFileNegativeAnswerCommand : OdetteCommand
		{
			public EndFileNegativeAnswerCommand(byte[] transmissionBuffer, int length)
				: base(transmissionBuffer, length)
			{
			} // ctor

			public OdetteAnswerReason AnswerReason
			{
				get { return (OdetteAnswerReason)ReadNumber(1, 2); }
				set { WriteNumber(1, 2, (int)value); }
			} // prop AnswerReason

			public abstract string ReasonText { get; set; }

			public sealed override char Signature => '5';
		} // class EndFileNegativeAnswerCommand

		#endregion

		#region -- class EndFileNegativeAnswerCommandV1 -----------------------------------

		private sealed class EndFileNegativeAnswerCommandV1 : EndFileNegativeAnswerCommand
		{
			public EndFileNegativeAnswerCommandV1(byte[] transmissionBuffer)
				: base(transmissionBuffer, 3)
			{
				Data[0] = (byte)Signature;
				WriteNumber(1, 2, 0);
				WriteNumber(3, 3, 0);
			} // ctor

			public EndFileNegativeAnswerCommandV1(byte[] transmissionBuffer, int length)
				: base(transmissionBuffer, length)
			{
			} // ctor

			public override string ReasonText
			{
				get { return GetReasonText(AnswerReason); }
				set { }
			} // prop ReasonText
		} // class EndFileNegativeAnswerCommandV1

		#endregion

		#region -- class EndFileNegativeAnswerCommandV2 -----------------------------------

		private sealed class EndFileNegativeAnswerCommandV2 : EndFileNegativeAnswerCommand
		{
			public EndFileNegativeAnswerCommandV2(byte[] transmissionBuffer)
				: base(transmissionBuffer, 6)
			{
				Data[0] = (byte)Signature;
				WriteNumber(1, 2, 0);
				WriteNumber(3, 3, 0);
			} // ctor

			public EndFileNegativeAnswerCommandV2(byte[] transmissionBuffer, int length)
				: base(transmissionBuffer, length)
			{
			} // ctor

			public override string ReasonText
			{
				get
				{
					var v = ReadUtf8(3);
					if (String.IsNullOrEmpty(v))
						v = GetReasonText(AnswerReason);
					return v;
				}
				set { Length = WriteUtf8(3, value); }
			} // prop ReasonText
		} // class EndFileNegativeAnswerCommandV2

		#endregion

		#region -- class EndSessionCommand ------------------------------------------------

		private abstract class EndSessionCommand : OdetteCommand
		{
			public EndSessionCommand(byte[] transmissionBuffer, int length)
				: base(transmissionBuffer, length)
			{
			} // ctor

			public override bool IsValid(out string error)
			{
				if (!IsValidFixed(Length - 1, "\x0D", "\x8D"))
				{
					error = "End of command is not valid.";
					return false;
				}
				else
					return base.IsValid(out error);
			} // func IsValid

			public OdetteEndSessionReasonCode ReasonCode
			{
				get { return (OdetteEndSessionReasonCode)ReadNumber(1, 2); }
				set { WriteNumber(1, 2, (int)value); }
			} // prop ReasonCode

			public virtual string ReasonText
			{
				get
				{
					string returnText = null;
					if (Length > 4)
						returnText = ReadUtf8(3);
					return String.IsNullOrEmpty(returnText) ? GetReasonText(ReasonCode) : returnText;
				}
				set { }
			} // prop ReasonText

			public sealed override char Signature => 'F';
		} // class EndSessionCommand

		#endregion

		#region -- class EndSessionCommandV1 ----------------------------------------------

		private sealed class EndSessionCommandV1 : EndSessionCommand
		{
			public EndSessionCommandV1(byte[] transmissionBuffer)
				: base(transmissionBuffer, 4)
			{
				Data[0] = (byte)Signature;
				WriteNumber(1, 2, 0);
				Data[3] = 0x0D;
			} // ctor

			public EndSessionCommandV1(byte[] transmissionBuffer, int length)
				: base(transmissionBuffer, length)
			{
			} // ctor

			public override bool IsValid(out string error)
			{
				if (!IsValidFixed(Length - 1, "\x0D", "\x8D"))
				{
					error = "End of command is not valid.";
					return false;
				}
				else
					return base.IsValid(out error);
			} // func IsValid

			public override string ReasonText
			{
				get { return base.ReasonText; }
				set { }
			} // prop ReasonText
		} // class EndSessionCommandV1

		#endregion

		#region -- class EndSessionCommandV2 ----------------------------------------------

		private sealed class EndSessionCommandV2 : EndSessionCommand
		{
			public EndSessionCommandV2(byte[] transmissionBuffer)
				: base(transmissionBuffer, 7)
			{
				Data[0] = (byte)Signature;
				WriteNumber(1, 2, 0);
				WriteNumber(3, 3, 0);
				Data[6] = 0x0D;
			} // ctor

			public EndSessionCommandV2(byte[] transmissionBuffer, int length)
				: base(transmissionBuffer, length)
			{
			} // ctor

			public override bool IsValid(out string error)
			{
				if (!IsValidFixed(Length - 1, "\x0D", "\x8D"))
				{
					error = "End of command is not valid.";
					return false;
				}
				else
					return base.IsValid(out error);
			} // func IsValid

			public override string ReasonText
			{
				get { return base.ReasonText; }
				set
				{
					var endAt = WriteUtf8(3, value);
					Data[endAt++] = 0x0D;
					Length = endAt;
				}
			} // prop ReasonText
		} // class EndSessionCommandV1

		#endregion

		#region -- class ChangeDirectionCommand -------------------------------------------

		private sealed class ChangeDirectionCommand : OdetteCommand
		{
			public ChangeDirectionCommand(byte[] transmissionBuffer)
				: base(transmissionBuffer, 1)
			{
				Data[0] = (byte)Signature;
			} // ctor

			public ChangeDirectionCommand(byte[] transmissionBuffer, int length)
				: base(transmissionBuffer, length)
			{
			} // ctor

			public override char Signature => 'R';
		} // class ChangeDirectionCommand

		#endregion

		#region -- class EndEndResponseCommand --------------------------------------------

		private abstract class EndEndResponseCommand : OdetteCommand, IOdetteFile, IOdetteFileEndToEndDescription
		{
			public EndEndResponseCommand(byte[] transmissionBuffer, int length)
				: base(transmissionBuffer, length)
			{
			} // ctor

			public string VirtualFileName
			{
				get { return ReadAscii(1, 26); }
				set { WriteAscii(1, 26, value); }
			} // prop VirtualFileName

			public abstract DateTime FileStamp { get; set; }

			public string UserData
			{
				get { return ReadAscii(48, 8); }
				set { WriteAscii(48, 8, value); }
			} // prop UserData

			public string Destination
			{
				get { return ReadAscii(56, 25); }
				set { WriteAscii(56, 25, value); }
			} // prop Destination

			public string Originator
			{
				get { return ReadAscii(81, 25); }
				set { WriteAscii(81, 25, value); }
			} // prop Originator

			string IOdetteFile.Originator => Destination; // destination is the orginal originator

			public abstract int ReasonCode { get; }
			public abstract string ReasonText { get; }

			IOdetteFile IOdetteFileEndToEndDescription.Name => this;

			public sealed override char Signature => 'E';
		} // class EndEndResponseCommand

		#endregion

		#region -- class EndEndResponseCommandV1 ------------------------------------------

		private sealed class EndEndResponseCommandV1 : EndEndResponseCommand
		{
			public EndEndResponseCommandV1(byte[] transmissionBuffer)
				: base(transmissionBuffer, 106)
			{
				Data[0] = (byte)Signature;
				WriteAscii(27, 9, String.Empty);
			} // ctor

			public EndEndResponseCommandV1(byte[] transmissionBuffer, int length)
				: base(transmissionBuffer, length)
			{
			} // ctor

			public override DateTime FileStamp
			{
				get { return DateTime.ParseExact(ReadAscii(36, 12), "yyMMddHHmmss", CultureInfo.InvariantCulture); }
				set { WriteAscii(36, 12, value.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture)); }
			} // prop FileStamp

			public override int ReasonCode => 0;
			public override string ReasonText => String.Empty;
		} // class EndEndResponseCommandV1

		#endregion

		#region -- class EndEndResponseCommandV2 ------------------------------------------

		private sealed class EndEndResponseCommandV2 : EndEndResponseCommand
		{
			public EndEndResponseCommandV2(byte[] transmissionBuffer)
				: base(transmissionBuffer, 110)
			{
				Data[0] = (byte)Signature;
				WriteAscii(27, 3, String.Empty);
				WriteUnsignedNumber(106, 2, 0);
				WriteUnsignedNumber(108, 2, 0);
			} // ctor

			public EndEndResponseCommandV2(byte[] transmissionBuffer, int length)
				: base(transmissionBuffer, length)
			{
			} // ctor

			public override DateTime FileStamp
			{
				get { return DateTime.ParseExact(ReadAscii(30, 18), "yyyyMMddHHmmssffff", CultureInfo.InvariantCulture); }
				set { WriteAscii(30, 18, value.ToString("yyyyMMddHHmmssffff", CultureInfo.InvariantCulture)); }
			} // prop FileStamp

			public int HashOffset => 106 + 2;
			public int HashLength => unchecked((int)ReadUnsignedNumber(106, 2));

			public int SignatureOffset => HashOffset + HashLength + 2;
			public int SignatureLength => unchecked((int)ReadUnsignedNumber(SignatureOffset - 2, 2));

			public byte[] FileHash
			{
				get { return ReadBytes(HashOffset); }
				set { WriteBytesWithResize(106, value); }
			} // prop FileHash

			public byte[] FileSignature
			{
				get { return ReadBytes(SignatureOffset - 2); }
				set { Length = WriteBytes(SignatureOffset - 2, value); }
			} // prop FileSignature

			public override int ReasonCode => 0;
			public override string ReasonText => String.Empty;
		} // class EndEndResponseCommandV2

		#endregion

		#region -- class NegativeEndResponseCommandV2 -------------------------------------

		private sealed class NegativeEndResponseCommandV2 : OdetteCommand, IOdetteFile, IOdetteFileEndToEndDescription
		{
			public NegativeEndResponseCommandV2(byte[] transmissionBuffer)
				: base(transmissionBuffer, 135)
			{
				Data[0] = (byte)Signature;
				WriteAscii(27, 6, String.Empty);
				WriteNumber(128, 3, 0);
				WriteUnsignedNumber(131, 2, 0);
				WriteUnsignedNumber(133, 2, 0);
			} // ctor

			public NegativeEndResponseCommandV2(byte[] transmissionBuffer, int length)
				: base(transmissionBuffer, length)
			{
			} // ctor

			public string VirtualFileName
			{
				get { return ReadAscii(1, 26); }
				set { WriteAscii(1, 26, value); }
			} // prop VirtualFileName

			public DateTime FileStamp
			{
				get { return DateTime.ParseExact(ReadAscii(33, 18), "yyyyMMddHHmmssffff", CultureInfo.InvariantCulture); }
				set { WriteAscii(33, 18, value.ToString("yyyyMMddHHmmssffff", CultureInfo.InvariantCulture)); }
			} // prop FileStamp

			public string Destination
			{
				get { return ReadAscii(51, 25); }
				set { WriteAscii(51, 25, value); }
			} // prop Destination

			public string Originator
			{
				get { return ReadAscii(76, 25); }
				set { WriteAscii(76, 25, value); }
			} // prop Originator

			string IOdetteFile.Originator => Destination; // destination is the orginal originator

			public string Creator
			{
				get { return ReadAscii(101, 25); }
				set { WriteAscii(101, 25, value); }
			} // prop Originator

			public int ReasonCode { get { return ReadNumber(126, 2); } set { WriteNumber(126, 2, value); } }
			public string ReasonText { get { return ReadUtf8(128); } set { WriteUtf8WithResize(128, value); } }

			public int HashOffset => 128 + ReadNumber(128, 3) + 5;
			public int HashLength => unchecked((int)ReadUnsignedNumber(HashOffset - 2, 2));

			public int SignatureOffset => HashOffset + HashLength + 2;
			public int SignatureLength => unchecked((int)ReadUnsignedNumber(SignatureOffset - 2, 2));

			public byte[] FileHash
			{
				get { return ReadBytes(HashOffset - 2); }
				set { WriteBytesWithResize(HashOffset - 2, value); }
			} // prop FileHash

			public byte[] FileSignature
			{
				get { return ReadBytes(SignatureOffset - 2); }
				set { Length = WriteBytes(SignatureOffset - 2, value); }
			} // prop FileSignature

			public sealed override char Signature => 'N';

			IOdetteFile IOdetteFileEndToEndDescription.Name => this;
			string IOdetteFileEndToEndDescription.UserData => String.Empty;
		} // class NegativeEndResponseCommandV2

		#endregion

		#region -- class ReadyToReceiveCommand --------------------------------------------

		private sealed class ReadyToReceiveCommand : OdetteCommand
		{
			public ReadyToReceiveCommand(byte[] transmissionBuffer)
				: base(transmissionBuffer, 1)
			{
				Data[0] = (byte)Signature;
			} // ctor

			public ReadyToReceiveCommand(byte[] transmissionBuffer, int length)
				: base(transmissionBuffer, length)
			{
			} // ctor

			public override char Signature => 'P';
		} // class RetryToReceiveCommand

		#endregion

		#region -- class SecurityChangeDirectionCommand -----------------------------------

		private sealed class SecurityChangeDirectionCommand : OdetteCommand
		{
			public SecurityChangeDirectionCommand(byte[] transmissionBuffer)
				: base(transmissionBuffer, 1)
			{
				Data[0] = (byte)Signature;
			} // ctor

			public SecurityChangeDirectionCommand(byte[] transmissionBuffer, int length)
				: base(transmissionBuffer, length)
			{
			} // ctor

			public override char Signature => 'J';
		} // class SecurityCangeDirectionCommand

		#endregion

		#region -- class AuthentificationChallengeCommand ---------------------------------

		private sealed class AuthentificationChallengeCommand : OdetteCommand
		{
			public AuthentificationChallengeCommand(byte[] transmissionBuffer)
				: base(transmissionBuffer, 3)
			{
				Data[0] = (byte)Signature;
				WriteUnsignedNumber(1, 2, 0);
			} // ctor

			public AuthentificationChallengeCommand(byte[] transmissionBuffer, int length)
				: base(transmissionBuffer, length)
			{
			} // ctor

			public int ChallengeOffset => 3;
			public int ChallengeLength => unchecked((int)ReadUnsignedNumber(1, 2));

			public byte[] Challenge
			{
				get { return ReadBytes(1); }
				set { Length = WriteBytes(1, value); }
			} // prop Challenge

			public override char Signature => 'A';
		} // class AuthentificationChallengeCommand

		#endregion

		#region -- class AuthentificationResponseCommand ----------------------------------

		private sealed class AuthentificationResponseCommand : OdetteCommand
		{
			public AuthentificationResponseCommand(byte[] transmissionBuffer)
				: base(transmissionBuffer, 21)
			{
				Data[0] = (byte)Signature;
			} // ctor

			public AuthentificationResponseCommand(byte[] transmissionBuffer, int length)
				: base(transmissionBuffer, length)
			{
			} // ctor

			public override bool IsValid(out string error)
			{
				if (Length != 21)
				{
					error = "Invalid length of command.";
					return false;
				}
				else
					return base.IsValid(out error);
			} // func IsValid

			public int DecryptedChallengeOffset => 1;
			public int DecryptedChallengeLength => 20;

			public byte[] DecryptedChallenge
			{
				get
				{
					var r = new byte[DecryptedChallengeLength];
					Array.Copy(Data, DecryptedChallengeOffset, r, 0, DecryptedChallengeLength);
					return r;
				}
				set
				{
					if (value == null || value.Length != 20)
						throw new ArgumentOutOfRangeException("decryptedChallenge");
					value.CopyTo(Data, 1);
				}
			} // proc SetDecryptedChallenge

			public override char Signature => 'S';
		} // class AuthentificationResponseCommand

		#endregion

		private readonly OdetteFileTransferProtocolItem item;
		private readonly IOdetteFtpChannel channel;
		private readonly LoggerProxy log;

		private OdetteFileService fileService = null; // current destination/file service after session start

		private OdetteVersion version = OdetteVersion.Rev20;
		private OdetteCapabilities capabilities = OdetteCapabilities.None;
		private int maximumDataExchangeBuffer = 99999;
		private int bufferCreditSize = 999;

		private byte[] receiveBuffer;
		private byte[] sendBuffer;

		#region -- Ctor/Dtor --------------------------------------------------------------

		public OdetteFtp(OdetteFileTransferProtocolItem item, IOdetteFtpChannel channel)
		{
			this.item = item;
			this.channel = channel;
			this.capabilities = channel.InitialCapabilities;

			this.log = LoggerProxy.Create(item.Log, channel.Name);
			log.Info("Oftp session start.");
		} // ctor

		public async Task DisconnectAsync()
		{
			log.Info("Oftp session disconnect.");

			// disconnect the channel
			await channel.DisconnectAsync();
			channel.Dispose();

			// disconnect file service
			Procs.FreeAndNil(ref fileService);
		} // proc Dispose

		#endregion

		#region -- Primitives - Error -----------------------------------------------------

		/// <summary>Creates a exception, if a unsupported packet is received.</summary>
		/// <param name="command"></param>
		/// <returns></returns>
		private Exception ThrowProtocolVoilation(OdetteCommand command)
		{
			if (command is EndSessionCommand)
			{
				var esid = (EndSessionCommand)command;
				log.Warn("Unexpected end session detected. Reason: [{0}] {1}", esid.ReasonCode, esid.ReasonText);
				return new OdetteRemoteEndException(esid.ReasonCode, esid.ReasonText);
			}
			else
			{
				var commandDescription = command.ToString();
				log.Warn("Protocol violation detected. Unexpected command: {0}", commandDescription);
				return new OdetteException(OdetteEndSessionReasonCode.ProtocolViolation, String.Format("Unexpected command: {0}", commandDescription));
			}
		} // func ThrowProtocolVoilation

		/// <summary>End the Session with the remote host, normally.</summary>
		/// <param name="reasonCode"></param>
		/// <param name="reasonText"></param>
		/// <returns></returns>
		private async Task<Exception> EndSessionAsync(OdetteEndSessionReasonCode reasonCode, string reasonText = null)
		{
			if (reasonText == null)
				reasonText = GetReasonText(reasonCode);

			// send the end message
			var esid = version == OdetteVersion.Rev20 ?
				(EndSessionCommand)CreateEmptyCommand<EndSessionCommandV2>() :
				(EndSessionCommand)CreateEmptyCommand<EndSessionCommandV1>();

			esid.ReasonCode = reasonCode;
			esid.ReasonText = reasonText;

			log.Info("Normal end session. Reason: [{0}] {1}", (int)reasonCode, reasonText);
			await SendCommandAsync(esid);
			return new OdetteAbortException(reasonCode, GetReasonText(reasonCode));
		} // proc EndSessionAsync

		private T LogFileServiceException<T>(string message, Func<T> f)
		{
			try
			{
				return f();
			}
			catch (OdetteFileServiceException e)
			{
				log.Warn(e);
				return default(T);
			}
			catch (Exception e)
			{
				log.Except(e);
				return default(T);
			}
		} // func LogFileServiceException

		private bool LogFileServiceException(string message, Action a)
		{
			try
			{
				a();
				return true;
			}
			catch (OdetteFileServiceException e)
			{
				log.Warn(message, e);
				return false;
			}
			catch (Exception e)
			{
				log.Except(message, e);
				return false;
			}
		} // func LogFileServiceException

		#endregion

		#region -- Receive/Send -----------------------------------------------------------

		private T CreateEmptyCommand<T>(Action<T> init = null)
			where T : OdetteCommand
		{
			var ci = typeof(T).GetConstructor(new Type[] { typeof(byte[]) });
			if (ci == null)
				throw new InvalidOperationException("invalid ctor.");

			if (sendBuffer == null || sendBuffer.Length != maximumDataExchangeBuffer)
				sendBuffer = new byte[maximumDataExchangeBuffer];

			var command = (T)ci.Invoke(new object[] { sendBuffer });
			init?.Invoke(command);
			return command;
		} // func CreateEmptyCommand

		private OdetteCommand CreateCommand(byte[] buffer, int length)
		{
			// create the command
			switch ((char)buffer[0])
			{
				case 'I':
					return new StartSessionReadyMessageCommand(buffer, length);
				case 'X':
					return new StartSessionCommand(buffer, length);
				case 'F':
					if (version == OdetteVersion.Rev20)
						return new EndSessionCommandV2(buffer, length);
					else
						return new EndSessionCommandV1(buffer, length);
				case 'H':
					if (version == OdetteVersion.Rev20)
						return new StartFileCommandV2(buffer, length);
					else
						return new StartFileCommandV1(buffer, length);
				case '2':
					if (version == OdetteVersion.Rev20)
						return new StartFilePositiveAnswerCommandV2(buffer, length);
					else
						return new StartFilePositiveAnswerCommandV1(buffer, length);
				case '3':
					if (version == OdetteVersion.Rev20)
						return new StartFileNegativeAnswerCommandV2(buffer, length);
					else
						return new StartFileNegativeAnswerCommandV1(buffer, length);
				case 'D':
					return new DataCommand(buffer, length);
				case 'C':
					return new SetCreditCommand(buffer, length);
				case 'T':
					if (version == OdetteVersion.Rev20)
						return new EndFileCommandV2(buffer, length);
					else
						return new EndFileCommandV1(buffer, length);
				case '4':
					return new EndFilePositiveAnswerCommand(buffer, length);
				case '5':
					if (version == OdetteVersion.Rev20)
						return new EndFileNegativeAnswerCommandV2(buffer, length);
					else
						return new EndFileNegativeAnswerCommandV1(buffer, length);
				case 'R':
					return new ChangeDirectionCommand(buffer, length);
				case 'E':
					if (version == OdetteVersion.Rev20)
						return new EndEndResponseCommandV2(buffer, length);
					else
						return new EndEndResponseCommandV1(buffer, length);
				case 'N':
					if (version == OdetteVersion.Rev20)
						return new NegativeEndResponseCommandV2(buffer, length);
					else
						throw new OdetteAbortException(OdetteEndSessionReasonCode.ProtocolViolation, "Invalid command.");
				case 'P':
					return new ReadyToReceiveCommand(buffer, length);
				case 'J':
					return new SecurityChangeDirectionCommand(buffer, length);
				case 'A':
					return new AuthentificationChallengeCommand(buffer, length);
				case 'S':
					return new AuthentificationResponseCommand(buffer, length);
				default:
					throw new ArgumentException("invalid command");
			}
		} // func CreateCommand

		/// <summary>Receive a command.</summary>
		/// <returns></returns>
		private async Task<OdetteCommand> ReceiveCommandAsync()
		{
			// re-create buffer
			if (receiveBuffer == null || receiveBuffer.Length != maximumDataExchangeBuffer)
				receiveBuffer = new byte[maximumDataExchangeBuffer];

			// read data from stream
			var recved = await channel.ReceiveAsync(receiveBuffer);
			if (recved == 0)
				throw new OdetteRemoteEndException(OdetteEndSessionReasonCode.TimeOut, "Connection closed.");

			// create the command object
			var command = CreateCommand(receiveBuffer, recved).CheckValid(false);
			if (item.IsDebugCommandsEnabled)
				log.Info("Recv: " + command.DebugString());

			return command;
		} // func ReceiveCommandAsync

		/// <summary>Receive specific command.</summary>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		private async Task<T> ReceiveCommandAsync<T>()
			where T : OdetteCommand
		{
			var command = await ReceiveCommandAsync();
			if (command is T)
				return (T)command;

			throw ThrowProtocolVoilation(command);
		} // func ReceiveCommand

		private async Task SendCommandAsync(OdetteCommand command)
		{
			// validate command
			command.CheckValid(true);

			// send the data
			if (command.Data != sendBuffer)
				throw new InvalidOperationException("Only the send buffer is accepted.");

			if (item.IsDebugCommandsEnabled)
				log.Info("Send: " + command.DebugString());

			await channel.SendAsync(command.Data, command.Length);
		} // func SendCommandAsync

		#endregion

		#region -- InitaitorSessionStartAsync ---------------------------------------------

		private void LogSessionInfo()
		{
			log.Info(String.Join(Environment.NewLine,
					"Session established.",
					"  Version: {0}",
					"  Destination: {1}",
					"  MaximumDataExchangeBuffer: {2:N0} bytes",
					"  BufferCreditSize: {3:N0} times",
					"  Capabilities: {4}"
				),
				version,
				fileService.DestinationId,
				maximumDataExchangeBuffer,
				bufferCreditSize,
				capabilities);
		} // proc LogSessionInfo

		private async Task InitaitorSessionStartAsync()
		{
			log.Info("Start session as initiator.");

			// wait for SSRM
			await ReceiveCommandAsync<StartSessionReadyMessageCommand>();

			// send own informationen
			await SendCommandAsync(
				CreateEmptyCommand<StartSessionCommand>(
					c =>
					{
						c.Version = version;
						c.InitiatorCode = item.OdetteId;
						c.Password = item.OdettePassword;
						c.DataExchangeBuffer = maximumDataExchangeBuffer;
						c.BufferCreditSize = bufferCreditSize;
						c.Capabilities = capabilities | OdetteCapabilities.Receive | OdetteCapabilities.Send; // we are optimistic, that we will support botth
						c.UserData = channel.UserData;
					}
				)
			);

			// wait for SSID
			var ssidCommand = await ReceiveCommandAsync<StartSessionCommand>();

			// check destination authentification, Rev1
			fileService = LogFileServiceException("Create file service.", () => item.CreateFileService(ssidCommand.InitiatorCode, ssidCommand.Password)); // create password
			if (fileService == null)
				throw await EndSessionAsync(OdetteEndSessionReasonCode.ResourcesNotAvailable, "Could not create file service.");

			// check capabilities
			if (!fileService.SupportsInFiles && !fileService.SupportsOutFiles) // no file valid Service
				throw await EndSessionAsync(OdetteEndSessionReasonCode.InvalidPassword, "Destination or password is not registered.");

			// set protocol parameter
			version = ssidCommand.Version;
			if (version != OdetteVersion.Rev13 && version != OdetteVersion.Rev20)
				throw await EndSessionAsync(OdetteEndSessionReasonCode.ModeOrCapabilitiesIncompatible);

			this.bufferCreditSize = ssidCommand.BufferCreditSize;
			this.maximumDataExchangeBuffer = ssidCommand.DataExchangeBuffer;
			this.capabilities = ssidCommand.Capabilities;

			LogSessionInfo();

			// Start secure authentification, if version is 2 and capabilities activated
			if (IsSecureAuthentification())
			{
				log.Info("Start authentification as initiator.");

				// send a change direction
				await SendCommandAsync(CreateEmptyCommand<SecurityChangeDirectionCommand>());
				await HandleSecurityChallengeListener();

				// change direction
				log.Info("Challange ok, change direction.");
				await ReceiveCommandAsync<SecurityChangeDirectionCommand>();
				await HandleSecurityChallengeSpeaker();

				log.Info("End authentification successful.");
			}

			log.Info("Start session successful.");
		} // func InitaitorSessionStartAsync

		#endregion

		#region -- ResponderSessionStartAsync ---------------------------------------------

		public async Task ResponderSessionStartAsync()
		{
			log.Info("Start session as responder.");

			// send ready
			await SendCommandAsync(CreateEmptyCommand<StartSessionReadyMessageCommand>());

			// wait for SSID
			var ssid = await ReceiveCommandAsync<StartSessionCommand>();

			// check destination and password
			fileService = LogFileServiceException("Create file service.", () => item.CreateFileService(ssid.InitiatorCode, ssid.Password));
			if (fileService == null || (!fileService.SupportsInFiles && !fileService.SupportsOutFiles)) // no file valid Service
				throw await EndSessionAsync(OdetteEndSessionReasonCode.InvalidPassword, "Destination or password is not registered.");

			if (fileService.SupportsInFiles)
				capabilities |= OdetteCapabilities.Receive;
			if (fileService.SupportsOutFiles)
				capabilities |= OdetteCapabilities.Send;

			// send SSID for the session
			var both = OdetteCapabilities.Receive | OdetteCapabilities.Send;
			if ((capabilities & both) != both && (ssid.Capabilities & both) != both)
			{
				if (((ssid.Capabilities | capabilities) & both) != both)
					throw await EndSessionAsync(OdetteEndSessionReasonCode.ModeOrCapabilitiesIncompatible);
			}

			if (ssid.Version != OdetteVersion.Rev13 && ssid.Version != OdetteVersion.Rev20)
				throw await EndSessionAsync(OdetteEndSessionReasonCode.ModeOrCapabilitiesIncompatible);

			// if one is not restartart able, clear restart
			CompareSessionStartFlag(ssid, OdetteCapabilities.Restart);
			CompareSessionStartFlag(ssid, OdetteCapabilities.BufferCompression);
			CompareSessionStartFlag(ssid, OdetteCapabilities.SpecialLogic);

			// Check secure authentification
			if (((ssid.Capabilities ^ capabilities) & OdetteCapabilities.SecureAuthentification) != 0)
				throw await EndSessionAsync(OdetteEndSessionReasonCode.SecureAuthenticationRequirementsIncompatible);

			version = ssid.Version;
			bufferCreditSize = ssid.BufferCreditSize;
			maximumDataExchangeBuffer = ssid.DataExchangeBuffer;

			// send combined SSID for the initiator
			await SendCommandAsync(
				CreateEmptyCommand<StartSessionCommand>(
					c =>
					{
						c.Version = version;
						c.InitiatorCode = item.OdetteId;
						c.Password = item.OdettePassword;
						c.DataExchangeBuffer = maximumDataExchangeBuffer;
						c.BufferCreditSize = bufferCreditSize;
						c.Capabilities = capabilities;
						c.UserData = channel.UserData;
					}
				)
			);

			LogSessionInfo();

			if (IsSecureAuthentification())
			{
				log.Info("Start authentification as responder.");

				await ReceiveCommandAsync<SecurityChangeDirectionCommand>();
				await HandleSecurityChallengeSpeaker();

				// change direction
				log.Info("Challange ok, change direction.");
				await SendCommandAsync(CreateEmptyCommand<SecurityChangeDirectionCommand>());
				await HandleSecurityChallengeListener();

				log.Info("End authentification successful.");
			}

			log.Info("Start session successful.");
		} // proc ResponderSessionStartAsync

		private void CompareSessionStartFlag(StartSessionCommand ssid, OdetteCapabilities flag)
		{
			if ((ssid.Capabilities & flag) != (capabilities & flag))
				capabilities = capabilities & ~flag;
		} // proc CompareSessionStartFlag

		#endregion

		#region -- Secure Authentification ------------------------------------------------

		private async Task HandleSecurityChallengeSpeaker()
		{
			// create a random number
			var randomNumber = CreateChallenge();

			// encrypt with the public key of the partner
			var encryptedChallenge = EncryptChallenge(randomNumber, item.FindCertificates(fileService.DestinationId, true).FirstOrDefault());
			if (encryptedChallenge == null)
				throw await EndSessionAsync(OdetteEndSessionReasonCode.InvalidChallengeResponse, "Challenge encryption failed.");

			await SendCommandAsync(CreateEmptyCommand<AuthentificationChallengeCommand>(c => c.Challenge = encryptedChallenge));
			var aurp = await ReceiveCommandAsync<AuthentificationResponseCommand>();

			if (!Procs.CompareBytes(aurp.DecryptedChallenge, randomNumber))
				throw await EndSessionAsync(OdetteEndSessionReasonCode.InvalidChallengeResponse, "Challenge is not equal.");
		} // proc HandleSecurityChallengeSpeaker

		private async Task HandleSecurityChallengeListener()
		{
			// certifcate is from destination (private key)
			var certificate = item.FindCertificates(fileService.DestinationId, false).FirstOrDefault();
			if (certificate == null || !certificate.HasPrivateKey || certificate.PrivateKey.KeyExchangeAlgorithm == null)
				throw await EndSessionAsync(OdetteEndSessionReasonCode.InvalidChallengeResponse, "No or invalid private key, to encrypt the challenge.");

			// authentifcation command challenge
			var auth = await ReceiveCommandAsync<AuthentificationChallengeCommand>();

			// decrypt and send back
			var decryptedChallenge = certificate == null ? null : DecryptChallenge(auth.Challenge, certificate);
			await SendCommandAsync(CreateEmptyCommand<AuthentificationResponseCommand>(
				c =>
				{
					if (decryptedChallenge == null || decryptedChallenge.Length != 20)
						Array.Clear(c.Data, c.DecryptedChallengeOffset, c.DecryptedChallengeLength);
					else
						Array.Copy(decryptedChallenge, 0, c.Data, c.DecryptedChallengeOffset, c.DecryptedChallengeLength);
				}
			));
		} // proc HandleSecurityChallengeListener

		private byte[] CreateChallenge()
		{
			var data = new byte[20];
			var r = new Random(Environment.TickCount);
			r.NextBytes(data);
			return data;
		} // func CreateChallange

		private byte[] EncryptChallenge(byte[] number, X509Certificate2 certifcate)
		{
			try
			{
				var cms = new EnvelopedCms(
					new ContentInfo(Oid.FromOidValue("1.2.840.113549.1.7.1", OidGroup.All), number),
					new AlgorithmIdentifier(Oid.FromOidValue("1.2.840.113549.3.7", OidGroup.All))
				);

				var cmsReceipt = new CmsRecipient(SubjectIdentifierType.IssuerAndSerialNumber, certifcate);
				cms.Encrypt(cmsReceipt);

				return cms.Encode();
			}
			catch (Exception e)
			{
				Log.Warn("Encrypt of challenge failed.", e);
				return null;
			}
		} // func EncryptChallenge

		private byte[] DecryptChallenge(byte[] challenge, X509Certificate2 certificate)
		{
			try
			{
				var cms = new EnvelopedCms();
				cms.Decode(challenge);
				cms.Decrypt(cms.RecipientInfos[0], new X509Certificate2Collection(certificate));

				return cms.ContentInfo.Content;
			}
			catch (Exception e)
			{
				Log.Warn("Decrypt of challenge failed.", e);
				return null;
			}
		} // func DecryptChallenge

		private bool IsSecureAuthentification()
			=> version == OdetteVersion.Rev20 && (capabilities & OdetteCapabilities.SecureAuthentification) != 0;

		#endregion

		#region -- SendFiles --------------------------------------------------------------

		private long RestartLength(OdetteFileFormat format, IOdetteFileReader outFile)
		{
			if (outFile is IOdetteFilePosition && (capabilities & OdetteCapabilities.Restart) != 0)
			{
				switch (format)
				{
					case OdetteFileFormat.Fixed:
					case OdetteFileFormat.Variable:
						return outFile.RecordCount;
					case OdetteFileFormat.Unstructured:
					case OdetteFileFormat.Text:
						return outFile.TotalLength >> 10;
					default:
						return 0L;
				}
			}
			else
				return 0L;
		} // func RestartLength

		private StartFileCommand CreateStartFileCommand(IOdetteFileReader outFile, out long restartLength)
		{
			var sfid = version == OdetteVersion.Rev20 ?
				(StartFileCommand)CreateEmptyCommand<StartFileCommandV2>() :
				(StartFileCommand)CreateEmptyCommand<StartFileCommandV1>();

			// common attributes
			var fileDescription = outFile.Name as IOdetteFileDescription;
			var filePosition = outFile as IOdetteFilePosition;

			sfid.VirtualFileName = outFile.Name.VirtualFileName;
			sfid.UserData = outFile.UserData;
			sfid.FileStamp = outFile.Name.FileStamp;
			sfid.FileSize = fileDescription?.FileSize ?? 0L;
			sfid.Format = fileDescription?.Format ?? OdetteFileFormat.Unstructured;
			sfid.RestartPosition = restartLength = RestartLength(sfid.Format, outFile);
			sfid.MaximumRecordSize = fileDescription?.MaximumRecordSize ?? 0;
			sfid.Destination = fileService.DestinationId;
			sfid.Originator = outFile.Name.Originator;

			// special handling for Rev2
			if (version == OdetteVersion.Rev20)
			{
				var sfid2 = sfid as StartFileCommandV2;
				sfid2.FileSizeUnpacked = fileDescription?.FileSizeUnpacked ?? sfid.FileSize;
				sfid2.SecurityLevel = OdetteSecurityLevels.None;
				sfid2.CipherSuite = 0;
				sfid2.Compressed = 0;
				sfid2.Enveloped = 0;
				sfid2.EerpSigned = false;
				sfid2.Description = fileDescription?.Description ?? String.Empty;
			}

			return sfid;
		} // func CreateStartFileCommand

		private EndFileCommand CreateEndFileCommand(IOdetteFileReader outFile)
		{
			var efid = version == OdetteVersion.Rev20 ?
				(EndFileCommand)CreateEmptyCommand<EndFileCommandV2>() :
				(EndFileCommand)CreateEmptyCommand<EndFileCommandV1>();

			var fileDescription = outFile.Name as IOdetteFileDescription;
			var format = fileDescription?.Format;
			if (format.HasValue)
			{
				switch (format.Value)
				{
					case OdetteFileFormat.Fixed:
					case OdetteFileFormat.Variable:
						efid.RecordCount = outFile.RecordCount;
						break;
					default:
						efid.RecordCount = 0;
						break;
				}
			}
			else
				efid.RecordCount = outFile.RecordCount;

			efid.UnitCount = outFile.TotalLength;

			return efid;
		} // func CreateStartFileCommand

		private async Task SendFileDataAsync(IOdetteFileReader outFile)
		{
			var allowCompression = (capabilities & OdetteCapabilities.BufferCompression) == OdetteCapabilities.BufferCompression;

			// transfer file data
			var endOfStream = false;
			while (!endOfStream)
			{
				//  send command buffer
				var currentCredit = bufferCreditSize;
				while (!endOfStream && currentCredit-- > 0)
					await SendCommandAsync(CreateEmptyCommand<DataCommand>(c => endOfStream = c.FillFromStream(outFile, allowCompression)));

				// wait for credit
				if (!endOfStream)
					await ReceiveCommandAsync<SetCreditCommand>();
			}

			// send eof
			await SendCommandAsync(CreateEndFileCommand(outFile));
		} // func SendFileDataAsync

		private async Task<bool> SendFileAsync(Func<IOdetteFileReader> f)
		{
			// create out file handle
			using (var outFile = LogFileServiceException("Open file for send.", f))
			{
				if (outFile == null)
					return false;

				using (var m = log.CreateScope(LogMsgType.Information, true, true))
				{
					m.WriteLine("Send file: {0}", outFile.Name.VirtualFileName);

					// try to send file
					long restartLength;
					await SendCommandAsync(CreateStartFileCommand(outFile, out restartLength));

					// wait for SFPA, SFNA
					var command = await ReceiveCommandAsync();
					if (command is StartFilePositiveAnswerCommand)
					{
						var c = (StartFilePositiveAnswerCommand)command;
						if (c.RestartPosition != 0) // close connection
						{
							var filePosition = outFile as IOdetteFilePosition;
							if (filePosition == null || restartLength < c.RestartPosition) // restart is not allowed
								throw await EndSessionAsync(OdetteEndSessionReasonCode.CommandContainedInvalidData, $"Restart position {c.RestartPosition} is invalid (expected <= {restartLength}).");
							else
							{
								var newPosition = filePosition.Seek(c.RestartPosition);
								m.WriteLine("File restart handled from {0} => {1}.", restartLength, newPosition);
							}
						}

						m.WriteLine("Send data.");
						await SendFileDataAsync(outFile);

						// end file actions
						command = await ReceiveCommandAsync();
						if (command is EndFilePositiveAnswerCommand)
						{
							var c2 = (EndFilePositiveAnswerCommand)command;
							m.WriteLine("Successful.");
							LogFileServiceException("Commit file.", outFile.SetTransmissionState);
							return c2.ChangeDirection;
						}
						else if (command is EndFileNegativeAnswerCommand)
						{
							var c2 = (EndFileNegativeAnswerCommand)command;

							m.SetType(LogMsgType.Warning)
								.WriteLine("File send failed: [{0}] {1}", c2.AnswerReason, c2.ReasonText);

							LogFileServiceException("Set error state.", () => outFile.SetTransmissionError(c2.AnswerReason, c2.ReasonText, true));
							return false;
						}
						else
						{
							m.SetType(LogMsgType.Error)
								.WriteLine("Protocol voilation.");

							throw ThrowProtocolVoilation(command);
						}
					}
					else if (command is StartFileNegativeAnswerCommand)
					{
						// file transmit failed
						var c = (StartFileNegativeAnswerCommand)command;

						m.SetType(LogMsgType.Warning)
							.WriteLine("File not accepted: [{0},r={2}] {1}", c.AnswerReason, c.ReasonText, c.RetryFlag);

						LogFileServiceException("Set error state.", () => outFile.SetTransmissionError(c.AnswerReason, c.ReasonText, c.RetryFlag));
						return false;
					}
					else
					{
						m.SetType(LogMsgType.Error)
							.WriteLine("Protocol voilation.");

						throw ThrowProtocolVoilation(command);
					}
				}
			}
		} // func SendFileAsync

		private async Task SendFileEndToEndAsync(IOdetteFileEndToEnd f)
		{
			if (f.ReasonCode == 0) // send EERP
			{
				Action<EndEndResponseCommand> initV1 =
					c =>
					{
						c.VirtualFileName = f.Name.VirtualFileName;
						c.Destination = fileService.DestinationId;
						c.Originator = item.OdetteId; // f.Name.Originator;
						c.FileStamp = f.Name.FileStamp;
						c.UserData = f.UserData;
					};
				Action<EndEndResponseCommandV2> initV2 =
					c =>
					{
						//c.FileHash;
						//c.FileSignature;
					};

				var eerp = version == OdetteVersion.Rev20 ?
					(EndEndResponseCommand)CreateEmptyCommand<EndEndResponseCommandV2>(
						c =>
						{
							initV1(c);
							initV2(c);
						}) :
					(EndEndResponseCommand)CreateEmptyCommand<EndEndResponseCommandV1>(initV1);

				// send and wait
				await SendCommandAsync(eerp);
			}
			else if (version == OdetteVersion.Rev20)
			{
				await SendCommandAsync(CreateEmptyCommand<NegativeEndResponseCommandV2>(
					c =>
					{
						c.VirtualFileName = f.Name.VirtualFileName;
						c.Destination = fileService.DestinationId;
						c.Originator = item.OdetteId; // f.Name.Originator;
						c.FileStamp = f.Name.FileStamp;
						c.Creator = item.OdetteId;
						c.ReasonCode = f.ReasonCode;
						c.ReasonText = f.ReasonText;
					}));
			}

			// wait for RTC
			await ReceiveCommandAsync<ReadyToReceiveCommand>();

			// mark that, the EE is sent
			LogFileServiceException("Commit for EndToEnd.", f.Commit);
		} // proc SendFileEndToEndAsync

		private async Task<bool> SendFilesAsync(IEnumerator<Func<IOdetteFileReader>> newOutFileList, IEnumerator<IOdetteFileEndToEnd> eerpInFileList)
		{
			// send file data
			while (newOutFileList.MoveNext())
			{
				if (await SendFileAsync(newOutFileList.Current))
					return true;
			}

			// send eerp's
			while (eerpInFileList.MoveNext())
				await SendFileEndToEndAsync(eerpInFileList.Current);

			return false;
		} // proc SendFilesAsync

		#endregion

		#region -- Receive Files ----------------------------------------------------------

		private async Task ReceiveFileAsync(StartFileCommand startFileCommand, bool changeDirectionRequest)
		{
			IOdetteFileWriter newFile;

			// create the file handle
			using (var m = log.CreateScope(LogMsgType.Information, true, true))
			{
				m.WriteLine("Receive file: {0}", startFileCommand.VirtualFileName);

				try
				{
					// re-check destination
					if (startFileCommand.Destination != item.OdetteId)
						throw new OdetteFileServiceException(OdetteAnswerReason.InvalidDestination, String.Format("Destination '{0}' is invalid (expected: {1}).", startFileCommand.Destination, item.OdetteId), false);

					var cmd2 = startFileCommand as StartFileCommandV2;
					if (cmd2 != null) // i support only level Version 1
					{
						if (cmd2.CipherSuite != 0)
							throw new OdetteFileServiceException(OdetteAnswerReason.CipherSuiteNotSupported);
						if (cmd2.SecurityLevel != OdetteSecurityLevels.None)
							throw new OdetteFileServiceException(OdetteAnswerReason.EncryptedFileNotAllowed);
						if (cmd2.Compressed != 0)
							throw new OdetteFileServiceException(OdetteAnswerReason.CompressionNotAllowed);
						if (cmd2.Enveloped != 0)
							throw new OdetteFileServiceException(OdetteAnswerReason.SignedFileNotAllowed);

						if (cmd2.EerpSigned)
							throw new OdetteFileServiceException(OdetteAnswerReason.SignedFileNotAllowed, "Signed eerp not allowed.");
					}

					newFile = fileService.CreateInFile(startFileCommand, startFileCommand.UserData); // add the file
					m.WriteLine("File service accepts the file.");
				}
				catch (Exception e)
				{
					var sfna = version == OdetteVersion.Rev20 ?
						(StartFileNegativeAnswerCommand)CreateEmptyCommand<StartFileNegativeAnswerCommandV2>() :
						(StartFileNegativeAnswerCommand)CreateEmptyCommand<StartFileNegativeAnswerCommandV1>();

					var e2 = e as OdetteFileServiceException;
					if (e2 == null)
					{
						sfna.AnswerReason = OdetteAnswerReason.UnspecifiedReason;
						sfna.ReasonText = e.Message;
						sfna.RetryFlag = true;

						m.WriteException(e);
					}
					else
					{
						sfna.AnswerReason = e2.ReasonCode;
						sfna.ReasonText = e2.ReasonText;
						sfna.RetryFlag = e2.RetryFlag;

						m.WriteWarning(e2);
					}

					await SendCommandAsync(sfna);
					return;
				}

				using (newFile)
				{
					// prepare answer
					var sfpa = version == OdetteVersion.Rev20 ?
						(StartFilePositiveAnswerCommand)CreateEmptyCommand<StartFilePositiveAnswerCommandV2>() :
						(StartFilePositiveAnswerCommand)CreateEmptyCommand<StartFilePositiveAnswerCommandV1>();

					// validate restart position
					if (startFileCommand.RestartPosition > 0)
					{
						// check for restart
						var filePosition = newFile as IOdetteFilePosition;
						if (filePosition == null || (capabilities & OdetteCapabilities.Restart) == 0)
							sfpa.RestartPosition = 0;
						else
							sfpa.RestartPosition = filePosition.Seek(startFileCommand.RestartPosition);

						m.WriteLine("Restart position handled from {0:N0} => {1:N0}.", startFileCommand.RestartPosition, sfpa.RestartPosition);
					}
					else
						sfpa.RestartPosition = 0;

					m.WriteLine("Receive data.");
					await SendCommandAsync(sfpa);

					// receive data
					var creditCounter = bufferCreditSize;
					var lastEoR = false;
					while (true)
					{
						var command = await ReceiveCommandAsync();
						if (command is DataCommand)
						{
							var data = (DataCommand)command;
							lastEoR = data.WriteToStream(newFile);
							if (--creditCounter <= 0)
							{
								await SendCommandAsync(CreateEmptyCommand<SetCreditCommand>());
								creditCounter = bufferCreditSize;
							}
						}
						else if (command is EndFileCommand)
						{
							try
							{
								if (!lastEoR)
									throw new OdetteFileServiceException(OdetteAnswerReason.InvalidByteCount, "Unexpected end of stream.");

								// commit the file
								m.WriteLine("File received. Commit the file in the file service.");
								var efid = (EndFileCommand)command;
								newFile.CommitFile(efid.RecordCount, efid.UnitCount);

								m.WriteLine("File received successful: {0:N0} bytes, {1:N0} records", newFile.TotalLength, newFile.RecordCount);

								// send successful receive
								await SendCommandAsync(CreateEmptyCommand<EndFilePositiveAnswerCommand>(c =>
								{
									c.ChangeDirection = changeDirectionRequest;
								}));
							}
							catch (Exception e)
							{
								var cmd = version == OdetteVersion.Rev20 ?
									(EndFileNegativeAnswerCommand)CreateEmptyCommand<EndFileNegativeAnswerCommandV2>() :
									(EndFileNegativeAnswerCommand)CreateEmptyCommand<EndFileNegativeAnswerCommandV1>();

								var e2 = e as OdetteFileServiceException;
								if (e2 == null)
								{
									cmd.AnswerReason = OdetteAnswerReason.UnspecifiedReason;
									cmd.ReasonText = e.Message;

									m.WriteException(e);
								}
								else
								{
									cmd.AnswerReason = e2.ReasonCode;
									cmd.ReasonText = e2.ReasonText;

									m.WriteWarning(e2);
								}

								await SendCommandAsync(cmd);
							}
							break;
						}
						else
						{
							m.SetType(LogMsgType.Error)
								.WriteLine("Protocol voilation.");

							throw ThrowProtocolVoilation(command);
						}
					}
				}
			}
		} // proc ReceiveFileAsync

		private async Task ReceiveEndEndResponseAsync(IOdetteFileEndToEndDescription command)
		{
			log.Info(command.ReasonCode != 0 ?
				"Receive negative end to end: {0} - [{1}] {2}" :
				"Receive positive end to end: {0}", command.Name.VirtualFileName, command.ReasonCode, command.ReasonText
			);

			LogFileServiceException("Update out file state.", () => fileService.UpdateOutFileState(command));
			await SendCommandAsync(CreateEmptyCommand<ReadyToReceiveCommand>());
		} // proc ReceiveEndEndResponseAsync

		private async Task<bool> ReceiveFilesAsync(bool changeDirectionRequest)
		{
			while (true)
			{
				var command = await ReceiveCommandAsync();

				if (command is StartFileCommand)
					await ReceiveFileAsync((StartFileCommand)command, changeDirectionRequest);
				else if (command is EndEndResponseCommand)
					await ReceiveEndEndResponseAsync((EndEndResponseCommand)command);
				else if (command is NegativeEndResponseCommandV2)
					await ReceiveEndEndResponseAsync((NegativeEndResponseCommandV2)command);
				else if (command is ChangeDirectionCommand) // speaker mode
					return true;
				else if (command is EndSessionCommand) // end session normal
				{
					var es = (EndSessionCommand)command;
					log.Info("Receive end session: [{0}] {1}", (int)es.ReasonCode, es.ReasonText);
					return false;
				}
				else
					throw ThrowProtocolVoilation(command);
			}
		} // func ReceiveFilesAsnyc

		#endregion

		#region -- Run --------------------------------------------------------------------

		public async Task RunAsync(bool initiator)
		{
			var speaker = initiator;
			var forceChangeDirection = true;

			// start the connection
			if (initiator)
				await InitaitorSessionStartAsync();
			else
				await ResponderSessionStartAsync();

			// create the lists
			var newOutFileList = fileService.GetOutFiles().GetEnumerator();
			var eerpInFileList = fileService.GetEndToEnd().GetEnumerator();

			while (true)
			{
				if (speaker)
				{
					// file transfer
					if (await SendFilesAsync(newOutFileList, eerpInFileList)) // order is important (first out files, than eerp)
					{
						log.Info("Change direction to listener.");
						await SendCommandAsync(CreateEmptyCommand<ChangeDirectionCommand>());
						speaker = false;
					}
					else if (forceChangeDirection)
					{
						log.Info("Change direction to listener.");
						await SendCommandAsync(CreateEmptyCommand<ChangeDirectionCommand>());
						speaker = false;

						forceChangeDirection = false;
					}
					else
						break;
				}
				else
				{
					if (await ReceiveFilesAsync(forceChangeDirection))
					{
						log.Info("Change direction to speaker.");
						forceChangeDirection = false;
						speaker = true;
					}
					else
						break;
				}
			}

			// end the session
			if (speaker)
			{
				log.Info("Send end session.");
				await EndSessionAsync(OdetteEndSessionReasonCode.None);
			}
		} // proc RunAsync

		#endregion

		/// <summary></summary>
		public LoggerProxy Log => log;
		/// <summary></summary>
		public IOdetteFtpChannel Channel => channel;

		// -- GetReasonText -------------------------------------------------------

		internal static string GetReasonText(OdetteEndSessionReasonCode reasonCode)
		{
			return reasonCode.ToString();
		} // func GetReasonText

		internal static string GetReasonText(OdetteAnswerReason reasonCode)
		{
			return reasonCode.ToString();
		} // func GetReasonText
	} // class OdetteFileTransferProtocol

	#endregion

	#region -- class OdetteFileTransferProtocolItem -------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	public class OdetteFileTransferProtocolItem : DEConfigLogItem
	{
		private static readonly XNamespace OdetteNamespace = "http://tecware-gmbh.de/dev/des/2014/odette";
		private static readonly XName xnCertificates = OdetteNamespace + "certificates";

		#region -- class ProtocolPool -----------------------------------------------------

		///////////////////////////////////////////////////////////////////////////////
		/// <summary></summary>
		private sealed class ProtocolPool : DEThreadLoop
		{
			private readonly OdetteFileTransferProtocolItem owner;

			public ProtocolPool(OdetteFileTransferProtocolItem owner)
				: base(owner, "Protocol")
			{
				this.owner = owner;
			} // ctor

			public Task StartProtocolAsync(IOdetteFtpChannel channel, bool initiator)
			{
				var protocol = new OdetteFtp(owner, channel);
				owner.AddProtocol(protocol);

				return Factory.StartNew(() => protocol.RunAsync(initiator))
					.ContinueWith(t => EndProtocolAsync(t.Result.Wait, protocol));
			} // proc StartProtocolAsync

			private void EndProtocolAsync(Action procWait, OdetteFtp protocol)
			{
				try
				{
					procWait();

					// disconnect protocol
					Task.Run(() => protocol.DisconnectAsync());
				}
				catch (Exception e)
				{
					protocol.Log.Except("Abnormal termination.", e);
				}
				finally
				{
					owner.RemoveProtocol(protocol);
				}
			} // proc EndProtocolAsync
		} // class ProtocolPool

		#endregion

		private ProtocolPool threadProtocol;
		private bool debugCommands = false;

		private DEList<OdetteFtp> activeProtocols;

		#region -- Ctor/Dtor --------------------------------------------------------------

		public OdetteFileTransferProtocolItem(IServiceProvider sp, string name)
			: base(sp, name)
		{
			threadProtocol = new ProtocolPool(this);

			this.activeProtocols = new DEList<OdetteFtp>(this, "tw_protocols", "Protocols");

			PublishItem(new DEConfigItemPublicAction("debugOn") { DisplayName = "Debug(on)" });
			PublishItem(new DEConfigItemPublicAction("debugOff") { DisplayName = "Debug(off)" });
		} // ctor

		protected override void Dispose(bool disposing)
		{
			if (disposing)
				Procs.FreeAndNil(ref threadProtocol);

			base.Dispose(disposing);
		} // proc Dispose

		#endregion

		#region -- Protocol List ----------------------------------------------------------

		private void AddProtocol(OdetteFtp oftp)
		{
			activeProtocols.Add(oftp);
		} // proc RemoveProtocol

		private void RemoveProtocol(OdetteFtp oftp)
		{
			activeProtocols.Remove(oftp);
		} // proc RemoveProtocol

		public bool IsActiveProtocol(string channelName)
		{
			return activeProtocols.FindIndex(o => o.Channel.Name == channelName) >= 0;
		} // func IsActiveProtocol

		#endregion

		#region -- FindCertificates -------------------------------------------------------

		/// <summary>Finds the certificates for the destination.</summary>
		/// <param name="destinationId"></param>
		/// <param name="partnerCertificate"><c>true</c> the public key of the partner, <c>false</c> the private key used for this destination.</param>
		/// <returns></returns>
		public IEnumerable<X509Certificate2> FindCertificates(string destinationId, bool partnerCertificate)
		{
			var collected = new List<X509Certificate2>();
			foreach (var x in Config.Elements(xnCertificates))
			{
				if (String.Compare(x.GetAttribute("destinationId", String.Empty), destinationId, StringComparison.OrdinalIgnoreCase) == 0)
				{
					try
					{
						foreach (var cert in ProcsDE.FindCertificate(x.GetAttribute(partnerCertificate ? "partner" : "my", String.Empty)))
							if (cert != null)
								collected.Add(cert);
					}
					catch (Exception e)
					{
						Log.Warn(e);
					}
				}
			}
			return collected;
		} // func FindCertificateFromDestination

		#endregion

		#region -- StartProtocolAsync, CreateFileService ----------------------------------

		public Task StartProtocolAsync(IOdetteFtpChannel channel, bool initiator)
			=> threadProtocol.StartProtocolAsync(channel, initiator);

		internal OdetteFileService CreateFileService(string destinationId, string password)
			=> new OdetteFileService(this, destinationId, password);

		#endregion

		[DEConfigHttpAction("debugOn", IsSafeCall = true, SecurityToken = SecuritySys)]
		private XElement SetDebugCommandsOn()
			=> SetDebugCommands(true);

		[DEConfigHttpAction("debugOff", IsSafeCall = true, SecurityToken = SecuritySys)]
		private XElement SetDebugCommandsOff()
			=> SetDebugCommands(false);

		[DEConfigHttpAction("debug", IsSafeCall = true, SecurityToken = SecuritySys)]
		private XElement SetDebugCommands(bool on = false)
		{
			debugCommands = on;
			OnPropertyChanged(nameof(IsDebugCommandsEnabled));
			return new XElement("return", new XAttribute("debug", debugCommands));
		} // func SetDebugCommands

		public string OdetteId => Config.GetAttribute("odetteId", String.Empty);
		public string OdettePassword => Config.GetAttribute("odettePassword", String.Empty);


		[
		PropertyName("tw_oftp_debug"),
		DisplayName("Debug Commands"),
		Description("Should the system log the in and outgoing oftp packets."),
		Category("OFTP"),
		Format("{0:XiB}")
		]
		public bool IsDebugCommandsEnabled => debugCommands;
	} // class OdetteFileTransferProtocolItem

	#endregion
}
