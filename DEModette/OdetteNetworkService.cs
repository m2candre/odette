﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TecWare.DE.Odette
{
	#region -- class OdetteNetworkException ---------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	public class OdetteNetworkException : Exception
	{
		public OdetteNetworkException(string message)
			: base(message)
		{
		} // ctor
  } // class OdetteNetworkException

	#endregion

	#region -- interface IOdetteFtpChannel ----------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	public interface IOdetteFtpChannel : IDisposable
	{
		/// <summary>Sends a disconnect to communication partner.</summary>
		/// <returns></returns>
		Task DisconnectAsync();

		/// <summary>Receive a command.</summary>
		/// <param name="buffer">Buffer that receives a command, it must have the maximum buffer size.</param>
		/// <returns>Returns the size of the received command or zero, if the channel is disconnected.</returns>
		Task<int> ReceiveAsync(byte[] buffer);
		/// <summary>Send a command.</summary>
		/// <param name="buffer">Buffer that contains a command.</param>
		/// <param name="filled">Length of the command.</param>
		/// <returns></returns>
		Task SendAsync(byte[] buffer, int filled);

		/// <summary>Unique name for channel, e.g. remote ip + protocol.</summary>
		string Name { get; }
		/// <summary>UserData, that will sent to the communication partner on connect.</summary>
		string UserData { get; }
		/// <summary>Returns the initial capabilities.</summary>
		OdetteCapabilities InitialCapabilities { get; }
	} // class IOdetteFtpChannel

	#endregion
}