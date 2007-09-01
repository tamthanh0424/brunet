using System;
using Brunet;
using System.Collections;
using System.Diagnostics;
using System.Security.Cryptography;

namespace Brunet {
  public abstract class Authorizer {
    public abstract bool Allow(SimNode n1, SimNode n2);
  }
  public class NatAuthorizer: Authorizer
  {
    public override bool Allow (SimNode n1, SimNode n2) {
      if (n1.Nat == NatType.Symmetric && n2.Nat == NatType.Cone) {
	return false;
      }
      if (n2.Nat == NatType.Symmetric && n1.Nat == NatType.Cone) {
	return false;
      }
      if (n2.Nat == NatType.Symmetric && n2.Nat == NatType.Symmetric) {
	return false;
      }
      return true;
    }
  }

  public class PairAuthorizer: Authorizer
  {
    protected static Random _rand = new Random();
    protected bool[,] _deny_matrix;
    public PairAuthorizer(int network_size, int bad_nodes)
    {
      _deny_matrix = new bool[network_size, network_size];
      for (int i = 0; i < network_size; i++) {
	for (int j = 0; j < network_size; j++) {
	  _deny_matrix[i, j] = false;
	}
      }
      int unique_bad_pairs = (int) (bad_nodes*bad_nodes/2.0);
      Console.WriteLine("Network size: {0}", network_size);
      Console.WriteLine("Bad nodes: {0}", bad_nodes);
      Console.WriteLine("Unique bad pairs: {0}", unique_bad_pairs);
      while (unique_bad_pairs > 0) {
	int x1, x2;
	do {
	  x1 = _rand.Next(0, network_size);
	  x2 = _rand.Next(0, network_size);	  
	} while (x1 == x2 || _deny_matrix[x1, x2] == true ||
		 _deny_matrix[x2, x1] == true);
	_deny_matrix[x1, x2] = _deny_matrix[x2, x1] = true;
	unique_bad_pairs -= 1;
      }
    }

    public override bool Allow(SimNode n1, SimNode n2) {
      return !(_deny_matrix[n1.Idx, n2.Idx]);
    }
  }

  public class SimPacket {
    public static readonly int MAX_TTL = 30;
    public static readonly int MAX_UPHILL_HOPS = 1; 

    public AHAddress Source;
    public AHAddress Destination;
    public RoutingMode Mode;
    public int Hops;
    public SimPacket(AHAddress source, AHAddress destination, RoutingMode mode) {
      this.Source = source;
      this.Destination = destination;
      this.Mode = mode;
    }
  }
  public enum RoutingMode : byte 
  {
    Greedy = 1,
    Exact = 2
  }


  public enum NatType : byte 
  {
    Cone = 1,
    Public = 2,
    Symmetric = 3
  } 

  public enum ConType : byte
  {
    Near = 1,
    Shortcut = 2
  }
  
  public class SimConnectionTable {
    protected ArrayList _con_list;
    protected Hashtable _ht;
    public SimConnectionTable() {
      _con_list = new ArrayList();
      _ht = new Hashtable();
    }
    public bool Add(AHAddress address, ConType t) {
      int idx = _con_list.BinarySearch(address);
      if (idx < 0) {
	idx = ~idx;
	_con_list.Insert(idx, address);
	if (!_ht.ContainsKey(t)) {
	  _ht[t] = new ArrayList();
	}
	((ArrayList) _ht[t]).Add(address);
	return true;
      } else {
	return false;
      }
    }
    public AHAddress GetByIndex(int idx) {
      if (_con_list.Count == 0) {
	return null;
      }
      idx %= _con_list.Count;
      if( idx < 0 ) {
	idx += _con_list.Count;
      }
      return (AHAddress) _con_list[idx];
    }
    public int IndexOf(AHAddress address) {
      int dest_idx = 0;
      if (_con_list.Count == 0) {
	dest_idx = ~dest_idx;
      }
      else {
	dest_idx = _con_list.BinarySearch(address);
      }
      return dest_idx;
    }
    public int GetCount(ConType t) {
      if (_ht.ContainsKey(t)) {
	return ((ArrayList) _ht[t]).Count;
      } else {
	return 0;
      }
    }
    public int GetTotalCount() {
      return _con_list.Count;
    }

    public ArrayList GetAllConnections() {
      return (ArrayList) _con_list.Clone();
    }

    public ArrayList GetConnections(ConType t) {
      if (_ht.ContainsKey(t)) {
	return (ArrayList) ((ArrayList) _ht[t]).Clone();
      } else {
	return null;
      }
    }
  }

  public class SimNode {
    protected static Random _rand = new Random();
    public AHAddress LocalAddress;
    public NatType Nat;
    public int Idx;
    protected SimConnectionTable _con_table;
    public SimConnectionTable ConnectionTable {
      get {
	return _con_table;
      }
    }
    protected int _network_size;

    public SimNode(AHAddress address, NatType nat, int idx) {
      this.LocalAddress = address;
      this.Nat = nat;
      this.Idx = idx;
      this._con_table = new SimConnectionTable();
    }
    
    protected void EstimateSize() {
      int net_size = -1;
      BigInteger least_dist = null;
      BigInteger greatest_dist = null;
      int shorts = 0;
      int structs = _con_table.GetTotalCount();
      if( structs + 1 > net_size ) {
	net_size = structs + 1;
      }
      /* We estimate the density of nodes in the address space,
       * and since we know the size of the whole address space,
       * we can use the density to estimate the number of nodes.
       */
      AHAddress local = this.LocalAddress;
      foreach(AHAddress c in _con_table.GetConnections(ConType.Near)) {
	BigInteger dist = local.DistanceTo(c);
	if( shorts == 0 ) {
	  //This is the first one
	  least_dist = dist;
	  greatest_dist = dist;
	}
	else {
	  if( dist > greatest_dist ) {
	    greatest_dist = dist;
	  }
	  if( dist < least_dist ) {
	    least_dist = dist;
	  }
	} 
	shorts++;
      }
      /*
       * Now we have the distance between the range of our neighbors
       */
      if( shorts > 0 ) {
	if ( greatest_dist > least_dist ) {
	  BigInteger width = greatest_dist - least_dist;
	  //Here is our estimate of the inverse density:
	  BigInteger inv_density = width/(shorts);
	  //The density times the full address space is the number
	  BigInteger full = Brunet.Address.Full;
	  BigInteger total = full / inv_density;
	  int total_int = total.IntValue();
	  if( total_int > net_size ) {
	    net_size = total_int;
          }
	}
      }
      _network_size = net_size;
    }

    public bool AddConnection(SimNode other, ConType t) {
      if (_con_table.Add(other.LocalAddress, t)) {
	other.AddConnection(this, t);
	EstimateSize();
	return true;
      } else {
	return false;
      }
    }
    
    public AHAddress NextHop(AHAddress from, SimPacket p, out bool deliverlocally) {
      AHAddress next_node = null;  
      deliverlocally = false;
      AHAddress dest = (AHAddress)p.Destination;
      if( p.Hops > SimPacket.MAX_TTL) {
	next_node = null;
      }
      else if ( LocalAddress.Equals(dest) ) {
	deliverlocally = true;
	next_node = null;
      }
      else {
	/*
	 * else we know hops < ttl, we can route:
	 * We now need to check the ConnectionTable
	 */
	int dest_idx = _con_table.IndexOf(dest);
	
	if( dest_idx >= 0 ) {
	  //We actually have a connection to this node:
	  next_node = _con_table.GetByIndex(dest_idx);
	} else if( _con_table.GetTotalCount() == 0) {
	  //We don't have any structured connections.  I guess we are the closest:
	  deliverlocally = true;
	  next_node = null;
	}
	else {
	  dest_idx = ~dest_idx;
	  /*
	   * Here are the right and left neighbors of the destination
	   * left is increasing, right is decreasing.
	   * Remember the ConnectionTable wraps around, so no need to worry
	   * about the size of index
	   */
	  int left_idx = dest_idx;
	  AHAddress left_n = _con_table.GetByIndex(left_idx);
	  int right_idx = dest_idx - 1;
	  AHAddress right_n = _con_table.GetByIndex(right_idx);
	  
	  //We check the a couple of connections:
	  BigInteger l_dist = dest.DistanceTo((AHAddress)left_n).abs();
	  BigInteger r_dist = dest.DistanceTo((AHAddress)right_n).abs();
	  AHAddress closest_node;
	  AHAddress other_node;
	  BigInteger closest_dist;
	  BigInteger other_dist;
	  if( l_dist < r_dist ) {
	    closest_node = left_n;
	    other_node = right_n;
	    closest_dist = l_dist;
	    other_dist = r_dist;
	  }
	  else {
	    closest_node = right_n;
	    other_node = left_n;
	    closest_dist = r_dist;
	    other_dist = l_dist;
	  }
	  if (p.Mode == RoutingMode.Greedy) {
	    BigInteger our_dist = dest.DistanceTo(this.LocalAddress).abs();
	    if( closest_dist < our_dist ) {
	      if( closest_node != from ) {
		next_node = closest_node;
	      } else {
		next_node = null;
	      }
	      deliverlocally = false;
	    } else {
	      next_node = null;
	      deliverlocally = true;
	    }
	  }
	  else //other routing modes 
	  {
	    //Compute our left neighbor.
	    int our_idx = _con_table.IndexOf(this.LocalAddress);
	    if( our_idx < 0 ) {
	      our_idx = ~our_idx;
	    }
	    AHAddress our_left_n = _con_table.GetByIndex(our_idx);
	    if( left_n == our_left_n ) {
	      /*
	       * We share a common left neighbor, so we should deliver locally
	       * This is the only case where we should deliver locally,
	       * otherwise there is at least one node on either side of the
	       * target, so one of them should probably get the packet.
	       */
	      deliverlocally = true;
	      //The next step should be the node on the "other side"
	      if(this.LocalAddress.IsLeftOf( dest ) ) {
		next_node = right_n;
	      }
	      else {
		next_node = left_n;
	      }
	      if(from == next_node) {
		next_node = null;
	      }
	    }
	    else if ( p.Hops == 0 ) {
	      /*
	       * This is the case that we sent the packet, and we are not
	       * a neighbor of the packet (the previous case)
	       * So, the closest_con must be good since we are the source
	       */
	      next_node = closest_node;
	    }
	    else if (p.Hops <= SimPacket.MAX_UPHILL_HOPS ) {
	      /*
	       * We will allow the packet to go uphill (get further from the source)
	       * at first, but this has to stop in order to prevent loops
	       *
	       * This may help the network form in the massive join case, or under
	       * heavy churn. @todo analyze approaches for improving stabilization
	       * in massively disordered cases.
	       */
	      if( closest_node != from ) {
		//Awesome.  This is an easy case...
		next_node = closest_node;
	      }
	      else {
		/*
		 * Look at the two next closest and choose the minimum distance of
		 * the three
		 */
		int sc_idx = -1;
		if( closest_node == right_n ) {
		  //move one over
		  sc_idx = right_idx - 1;
		}
		else {
		  //Must be the left:
		  sc_idx = left_idx + 1;
		}
		AHAddress second_closest = _con_table.GetByIndex(sc_idx);
		BigInteger second_dist =
		  dest.DistanceTo( (AHAddress)second_closest).abs();
		if( second_dist < other_dist ) {
		  other_node = second_closest;
		}
		if( other_node != from ) {
		  //If we only have one neighbor,
		  //other and closest might be the same
		  next_node = other_node;
		}
		else {
		  //We just can't win...
		  next_node = null;
		}
	      }
	    }
	    else {
	      /*
	       * This is the case where we are not a neighbor of the destination
	       * according to our table, and the packet has taken at least 2 hops.
	       */
	      deliverlocally = false;
	      if( ( closest_node == from ) 
		  && ( other_node != from ) ) {
		closest_dist = other_dist;
		closest_node = other_node;
	      }
	      AHAddress prev = from;
	      if( prev != null ) {
		BigInteger prev_dist = dest.DistanceTo( (AHAddress)prev ).abs();
		if( closest_dist >= prev_dist ) {
		  //Don't send it if you can't get it closer than it was before
		  next_node = null;
		}
		else {
		  next_node = closest_node;
		}
	      }
	      else {
		//This is the case that we don't have a connection
		//on the Edge the packet came from, this shouldn't happen,
		//but it is not a disaster.
		next_node = closest_node;
	      }
	    }//End of non-neareast neighbor case
	  }//End of Annealing case	    
	} //End of the case where we had to find a near route
      }
      if(p.Mode == RoutingMode.Exact) {
	if(this.LocalAddress.Equals(dest) ) {
          deliverlocally = true;
          next_node = null;
        }
        else {
          deliverlocally = false;
        }
      }
      return next_node;
    }
    
    public AHAddress ComputeShortcutTarget() {
      AHAddress local_address = this.LocalAddress;
      int network_size = _network_size;
      double logN = (double)(Brunet.Address.MemSize * 8);
      double logk = Math.Log( (double) network_size, 2.0 );
      double p = _rand.NextDouble();
      double ex = logN - (1.0 - p)*logk;
      int ex_i = (int)Math.Floor(ex);
      double ex_f = ex - Math.Floor(ex);
      //Make sure 2^(ex_long+1)  will fit in a long:
      int ex_long = ex_i % 63;
      int ex_big = ex_i - ex_long;
      ulong dist_long = (ulong)Math.Pow(2.0, ex_long + ex_f);
      //This is 2^(ex_big):
      BigInteger big_one = 1;
      BigInteger dist_big = big_one << ex_big;
      BigInteger rand_dist = dist_big * dist_long;

      // Add or subtract random distance to the current address
      BigInteger t_add = local_address.ToBigInteger();

      // Random number that is 0 or 1
      if( _rand.Next(2) == 0 ) {
        t_add += rand_dist;
      }
      else {
        t_add -= rand_dist;
      }

      BigInteger target_int = new BigInteger(t_add % Address.Full);
      return new AHAddress(target_int);
    }
  }


  public class Simulator {
    protected static Random _rand = new Random();
    public static int ComputeOverlap(ArrayList list1, ArrayList list2) {
      int count = 0;
      foreach(AHAddress x in list1) {
	if (list2.Contains(x)) {
	  count += 1;
	}
      }
      return count;
    }
    public static void Main(string []args) {
      int network_size = Int32.Parse(args[0].Trim());
      int num_bad_nodes = Int32.Parse(args[1].Trim());
      ArrayList sorted_node_list = new ArrayList();
      Hashtable address_to_node = new Hashtable();
      Authorizer authorizer = new PairAuthorizer(network_size, num_bad_nodes);
      int node_idx = 0;
      for (int i = 0; i < network_size; i++) {
	AHAddress address = new AHAddress(new RNGCryptoServiceProvider());
	SimNode node = new SimNode(address, NatType.Public, node_idx++);
	int idx = sorted_node_list.BinarySearch(address);
	if (idx < 0) {
	  idx = ~idx;
	} else {
	  Console.Error.WriteLine("Duplicate node");
	}
	sorted_node_list.Insert(idx, address);
	address_to_node[address] = node;
      }
      Console.WriteLine("Nodes created: {0}.", network_size);

      //
      // Place nears.
      //
      int missing_near = 0;
      int missing_next_near = 0;
      for (int i = 0; i < network_size; i++) {
	SimNode node_1 = (SimNode) address_to_node[(AHAddress) sorted_node_list[i]];
	SimNode node_2 = (SimNode) address_to_node[(AHAddress) sorted_node_list[(i + 1)%network_size]];
	SimNode node_3 = (SimNode) address_to_node[(AHAddress) sorted_node_list[(i + 2)%network_size]];
	
	if (authorizer.Allow(node_1, node_2)) {
	  node_1.AddConnection(node_2, ConType.Near);
	} else {
	  //Console.WriteLine("Cannot add near: {0} <-> {1}", node_1.LocalAddress, node_2.LocalAddress);
	  missing_near += 1;
	}

	if (authorizer.Allow(node_1, node_3)) {
	  node_1.AddConnection(node_3, ConType.Near);
	} else {
	  //Console.WriteLine("Cannot add near: {0} <-> {1}", node_1.LocalAddress, node_3.LocalAddress);
	  missing_next_near += 1;
	}
      }
      Console.WriteLine("Near connections added.");
      Console.WriteLine("Missing near: {0}.", missing_near);
      Console.WriteLine("Missing next near: {0}.", missing_next_near);      

      //
      // Place shortcuts.
      //
      int max_sc = (int) Math.Log( (double) network_size, 2.0 );
      for (int iter = 0; iter < 10*max_sc; iter += 1) {
	Console.WriteLine("iter: {0}", iter);
	foreach (SimNode n in address_to_node.Values) {
	  int logk = (int) Math.Log( (double) network_size, 2.0 );
	  if (n.ConnectionTable.GetCount(ConType.Shortcut) > logk) {
	    continue;
	  }
	  AHAddress t_addr = n.ComputeShortcutTarget();
	  int idx = sorted_node_list.BinarySearch(t_addr);
	  if (idx < 0) {
	    idx = ~idx;
	    idx = idx%network_size;
	  }
	  SimNode t_node = (SimNode) address_to_node[(AHAddress) sorted_node_list[idx]];
	  if (authorizer.Allow(n, t_node)) {
	    n.AddConnection(t_node, ConType.Shortcut);
	  } else {
	    //Console.WriteLine("Cannot add shortcut: {0} <-> {1}", n.LocalAddress, t_node.LocalAddress);
	  }
	}
      }

      Console.WriteLine("Shortcuts added.");
      ArrayList shortcut_dist = new ArrayList();
      //
      // Study the distribution of shortcut connections.
      //
      foreach(SimNode n in address_to_node.Values) {
	int my_sorted_idx = sorted_node_list.BinarySearch(n.LocalAddress);
	foreach(AHAddress c in n.ConnectionTable.GetConnections(ConType.Shortcut)) {
	  int your_sorted_idx = sorted_node_list.BinarySearch(c);
	  int d;
	  if (my_sorted_idx > your_sorted_idx) {
	    int temp = my_sorted_idx;
	    my_sorted_idx = your_sorted_idx;
	    your_sorted_idx = temp;
	  }
	  //your_sorted_idx greater than my_sorted_idx
	  d = your_sorted_idx - my_sorted_idx;
	  if (d > sorted_node_list.Count/2) {
	    d = sorted_node_list.Count - d;
	  }	  

	  int idx = shortcut_dist.BinarySearch(d);
	  if (idx < 0) {
	    idx = ~idx;
	  }
	  shortcut_dist.Insert(idx, d);
	}
      }
      //
      // Compute the distance to which each shortcut goes.
      //
      ArrayList shortcut_prob = new ArrayList();
      ArrayList shortcut_cumul = new ArrayList();
      int curr_dist = -1;
      int curr_count = 0;
      int cumul_count = 0;
      foreach(int d in shortcut_dist) {
	if (curr_dist < 0) {
	  curr_dist = d;
	  curr_count = 1;
	  cumul_count = 1;
	  continue;
	}
	if (d == curr_dist) {
	  curr_count += 1;
	  cumul_count += 1;
	  continue;
	}
	if (d != curr_dist) {
	  Trace.WriteLineIf(d > curr_dist, "Error in distance cumulative distribution");
	  shortcut_prob.Add(new object[] {curr_dist, curr_count});
	  shortcut_cumul.Add(new object[] {curr_dist, cumul_count});
	  curr_dist = d;
	  curr_count = 1;
	  cumul_count += 1;
	}
      }
      if (curr_dist > 0) {
	shortcut_prob.Add(new object[] {curr_dist, curr_count});	
	shortcut_cumul.Add(new object[] {curr_dist, cumul_count});	
      }

      Console.WriteLine("Computing probability distribution");
      foreach(object[] o in shortcut_prob) {
	Console.WriteLine("{0} {1}", (int) o[0], (int) o[1]);
      }

      Console.WriteLine("Computing cumulative distribution");
      foreach(object[] o in shortcut_cumul) {
	Console.WriteLine("{0} {1}", (int) o[0], (int) o[1]);
      }


      Console.WriteLine("Analysing connections per node");
      double sum = 0.0;
      int max_con = 0;
      Hashtable dist = new Hashtable();
      foreach(SimNode n in address_to_node.Values) {
	int count = n.ConnectionTable.GetTotalCount();
	sum += count;
	if (count > max_con) {
	  max_con = count;
	}
	if (dist.ContainsKey(count)) {
	  int c = (int) dist[count];
	  dist[count] = c + 1;
	} else {
	  dist[count] = 1;
 	}
      }
      for (int i = 0; i <= max_con; i++) {
	Console.WriteLine("{0} {1}", i, dist.ContainsKey(i)? dist[i]:0);
      }

      //
      // How probable are common connections between neighbors.
      //

      for (int i = 0; i < network_size; i++) {
	SimNode node_1 = (SimNode) address_to_node[(AHAddress) sorted_node_list[i]];
	SimNode node_2 = (SimNode) address_to_node[(AHAddress) sorted_node_list[(i + 1)%network_size]];
	SimNode node_3 = (SimNode) address_to_node[(AHAddress) sorted_node_list[(i + 2)%network_size]];
	//check n1 and n2 for the strength of overlap
	if (!authorizer.Allow(node_1, node_2)) {
	  int first = ComputeOverlap(node_1.ConnectionTable.GetAllConnections(), node_2.ConnectionTable.GetAllConnections());
	  Console.WriteLine("first overlap: {0}", first);
	}

	//check n1 and n3 for the strength of overlap
	if (!authorizer.Allow(node_1, node_3)) {
	  int second = ComputeOverlap(node_1.ConnectionTable.GetAllConnections(), node_3.ConnectionTable.GetAllConnections());
	  Console.WriteLine("second overlap: {0}", second);
	}
      }


      //
      // Analyse exact routing.
      //
      Console.WriteLine("Analysing exact routing");
      int broken_routes = 0;
      foreach (SimNode n1 in address_to_node.Values) {
	foreach (SimNode n2 in address_to_node.Values) {
	  SimPacket p = new SimPacket(n1.LocalAddress, n2.LocalAddress, RoutingMode.Exact);
	  SimNode curr = n1;
	  AHAddress prev_addr = null;
	  while (true) {
	    bool deliverlocally;
	    AHAddress next = curr.NextHop(prev_addr, p, out deliverlocally);
	    if (next == null) {
	      break;
	    }
	    prev_addr = curr.LocalAddress;
	    curr = (SimNode) address_to_node[next];
	    p.Hops += 1;
	  }
	  if (curr != n2) {
	    broken_routes += 1;
	  }
	}
      }
      Console.WriteLine("Broken paths for exact: {0}", broken_routes);
      Console.WriteLine("Analysing greedy routing");      
      //
      // Analyse greedy routing for keys. (large number of keys).
      //
      int inconsistent = 0;
      for (int i = 0; i < 10000; i++) {
	AHAddress key_target = new AHAddress(new RNGCryptoServiceProvider());
	if (i%100 == 0) {
	  Console.WriteLine("{0}: {1} ({2} inconsistent)", i, key_target, inconsistent);
	}
	// source the key at different nodes in the network.
	AHAddress target = null;
	foreach (SimNode n in address_to_node.Values) {
	  SimPacket p = new SimPacket(n.LocalAddress, key_target, RoutingMode.Greedy);
	  SimNode curr = n;
	  AHAddress prev_addr = null;
	  while (true) {
	    bool deliverlocally;
	    AHAddress next = curr.NextHop(prev_addr, p, out deliverlocally);
	    if (deliverlocally) {
	      Trace.WriteLineIf(next != null, "Error in greedy routing. Next hop following delivery");
	      if (target == null) {
		target = curr.LocalAddress;
	      } else {
		if (target != curr.LocalAddress) {
		  inconsistent++;
		}
	      }
	      break;
	    }

	    if (next == null) {
	      break;
	    }
	    
	    prev_addr = curr.LocalAddress;
	    curr = (SimNode) address_to_node[next];
	    p.Hops += 1;
	  }
	}
      }
      Console.WriteLine("Inconsistently routed keys: {0}", inconsistent);
    }
  }
}
