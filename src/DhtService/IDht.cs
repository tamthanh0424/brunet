/*
Copyright (C) 2008  David Wolinsky <davidiw@ufl.edu>, University of Florida

This program is free software; you can redistribute it and/or
modify it under the terms of the GNU General Public License
as published by the Free Software Foundation; either version 2
of the License, or (at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program; if not, write to the Free Software
Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
*/

using System;
using System.Text;
using Brunet.DistributedServices;
using System.Collections;

namespace Brunet.Rpc {
  /// <summary>
  /// A Generic Interface for Dht operations
  /// </summary>
  public interface IDht {
    /// <summary>
    /// Get Dht values
    /// </summary>    
    IDictionary[] Get(byte[] key);

    /// <summary>
    /// Places value in Dht if it is a unique key
    /// </summary>    
    /// <returns>true if successful</returns>
    bool Create(byte[] key, byte[] value, int ttl);

    /// <summary>
    /// Places a value in Dht indexed by its key    
    /// <returns>true if successful</returns>
    bool Put(byte[] key, byte[] value, int ttl);

    /**
     * @return: token for ContinueGet
     */
    byte[] BeginGet(byte[] key);
    IDictionary ContinueGet(byte[] token);
    void EndGet(byte[] token);

    IDictionary GetDhtInfo();
  }
}
