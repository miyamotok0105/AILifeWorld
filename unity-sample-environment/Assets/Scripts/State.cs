﻿using UnityEngine;
using System.Collections;

namespace MLPlayer {
	public class State {
		public float reward;
		public bool endEpisode;
		public byte[][] image;
		public byte[][] depth;
		public float[][] gene;  // add Naka
		public float[] rewards; // add Naka
		public int agent_id;    // add Naka
		public void Clear() {
			reward = 0;
			endEpisode = false;
			image   = null;
			depth   = null;
			rewards = null;
		}
	}
}