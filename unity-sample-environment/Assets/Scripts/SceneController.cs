﻿using UnityEngine;
using System.Collections.Generic;
using System.Threading;
using MsgPack;

namespace MLPlayer {
	public class SceneController : MonoBehaviour {

		// singleton
		protected static SceneController instance;
		public static SceneController Instance {
			get {
				if(instance == null) {
					instance = (SceneController) FindObjectOfType(typeof(SceneController));
					if (instance == null) {
						Debug.LogError("An instance of " + typeof(SceneController) + 
						               " is needed in the scene, but there is none.");
					}
				}
				return instance;
			}
		}

		enum CommunicationMode {ASYNC, SYNC}
		[SerializeField] CommunicationMode communicationMode;

		[SerializeField] string domain;
		[SerializeField] string path;
		[SerializeField] int port;
		[SerializeField] float cycleTimeStepSize;
		[SerializeField] float episodeTimeLength;
		[Range(0.1f, 10.0f)]
		[SerializeField] float timeScale = 1.0f;
		
		[SerializeField] List<Agent> agents;
		private List<IAIClient> clients;
		private List<Vector3> firstLocation;
		[SerializeField] Environment environment;
		private float lastSendTime;
		private float episodeStartTime = 0f;
		private int agentReceiveCounter;
		MsgPack.BoxingPacker packer = new MsgPack.BoxingPacker ();
		private int NumberOfParameter = 3; // add Naka
		
		public static ManualResetEvent received = new ManualResetEvent(false);
		private Mutex mutAgent;

		string GetUrl(string domain, int port, string path) {
			return "ws://" + domain + ":" + port.ToString () + "/" + path;
		}

        UnityEngine.UI.RawImage[] rawImages = null;
        public void SetRawImage(int index, RenderTexture tex)
        {
            if (rawImages == null)
                CreateRawImages();
            rawImages[index].texture = tex;
        }

        void CreateRawImages()
        {
            rawImages = new UnityEngine.UI.RawImage[agents.Count];
            for (int i = 0; i < agents.Count; ++i)
            {
                var a = agents[i];
                var v = GameObject.Instantiate(_viewPrefab);
                v.transform.SetParent(_canvas.transform);
                var t = v.GetComponent<RectTransform>();
                Vector3 p = new Vector3(50, 300 - 80 * i, 0);
                t.position = p;
                rawImages[i] = v.GetComponent<UnityEngine.UI.RawImage>();
            }
        }

        void Start () {
            if (rawImages == null)
                CreateRawImages();

            clients = new List<IAIClient> ();
			firstLocation = new List<Vector3> ();
			foreach (var agent in agents) {
				firstLocation.Add (agent.transform.position);
			}

			if (communicationMode == CommunicationMode.SYNC) {
				int cnt = 0;
				foreach (var agent in agents) {
					clients.Add (
						new AIClient (GetUrl(domain, port + cnt, path),
					                      OnMessage, agent));
					cnt++;
				}
			} else {
				Application.targetFrameRate = (int)Mathf.Max(60.0f, 60.0f * timeScale);
				int cnt = 0;
				foreach (var agent in agents) {
					clients.Add (new AIClientAsync (GetUrl(domain, port + cnt, path)));
					cnt++;
				}
			}

			StartNewEpisode ();
			lastSendTime = -cycleTimeStepSize;
			
			mutAgent = new Mutex();

			if (communicationMode == CommunicationMode.ASYNC && agents.Count > 1) {
				Debug.LogError ("not supprted multi agent ASYNC mode");
				throw new System.NotImplementedException ();
				Application.Quit();
			}
        }

        void OnMessage(byte[] msg, Agent agent) {
			mutAgent.WaitOne();
			agentReceiveCounter++;
			mutAgent.ReleaseMutex();

			agent.action.Set ((Dictionary<System.Object, System.Object>)packer.Unpack (msg));
			if (agent.state.endEpisode) {
			   agent.SetGene ((Dictionary<System.Object, System.Object>)packer.Unpack (msg)); // add Naka
			}
			if (agentReceiveCounter == agents.Count) {
				received.Set();
			}
		}
		
		public void TimeOver() {
			foreach (var agent in agents) {
				agent.EndEpisode ();
			}
		}

		void StartNewEpisode() {
			episodeStartTime = Time.time;
			
			environment.OnReset ();
			for (int i=0; i<agents.Count; i++) {
				agents[i].transform.position = firstLocation[i];
				agents[i].StartEpisode ();
			}
		}

		void FixedUpdate() {
			if (communicationMode == CommunicationMode.SYNC) {
				Time.timeScale = timeScale;
				if (lastSendTime + cycleTimeStepSize <= Time.time) {
					lastSendTime = Time.time;
					
					if (Time.time - episodeStartTime > episodeTimeLength) {
						TimeOver ();
					}
					
					// TODO all agents have same value
					if (agents [0].state.endEpisode) {
						StartNewEpisode ();
						for (int i=0; i<agents.Count; i++) {
						    agents[i].ChangeScale();
						}
					}
					
					agentReceiveCounter = 0;
					received.Reset ();
					for (int i = 0; i < agents.Count; i++) {
						agents [i].UpdateState ();

						agents [i].state.rewards = new float[agents.Count];  // Set rewards
						float[] rewards = new float[agents.Count];
						for (int j=0; j<agents.Count; j++) {
						    agents [i].state.rewards[j] = agents[j].state.reward;
						}

						agents [i].state.agent_id = i;                            // Set agent_id
						
						Vector3[] scales = new Vector3[agents.Count];             // Set gene
						for (int j=0; j<agents.Count; j++) {
						    scales[j] = agents[j].transform.localScale;
						}
						agents[i].state.gene = new float[agents.Count][];
						for (int j=0; j<agents.Count; j++) {
			    			    float[] xyz = new float[NumberOfParameter];
			    			    for (int k=0; k<NumberOfParameter; k++) {
			    			    	xyz[k] = (float)scales[j][k];
			    			    }
			    			    agents[i].state.gene[j] = xyz;
						}

						clients [i].PushAgentState (agents [i].state);
					}
					received.WaitOne ();
					
					foreach (var agent in agents) {
						agent.ResetState ();
					}
				}
			}
		}
		
		
		void Update() {
			if (communicationMode == CommunicationMode.ASYNC) {
				Application.targetFrameRate = (int)Mathf.Max (60.0f, 60.0f * timeScale);
				
				for (int i = 0; i < agents.Count; i++) {
					byte[] msg = clients [i].PopAIMessage ();
					if (msg != null) {
						var packer = new MsgPack.BoxingPacker ();
						agents [i].action.Set ((Dictionary<System.Object, System.Object>)packer.Unpack (msg));
						agents [i].ResetState ();
						Time.timeScale = timeScale;
					}
				}
				
				if (lastSendTime + cycleTimeStepSize <= Time.time) {
					lastSendTime = Time.time;
					
					if (Time.time - episodeStartTime > episodeTimeLength) {
						TimeOver ();
					}
					
					// TODO all agents have same value
					if (agents [0].state.endEpisode) {
					   StartNewEpisode ();
					}
					
					for (int i = 0; i < agents.Count; i++) {
						agents [i].UpdateState ();
						// <!> Must Get Parametar (add Naka)
						agents [i].state.rewards = new float[3]; // 3 -> number of parametar
						for (int j=0; j<3; j++) { // same
						    agents [i].state.rewards[j] = agents[j].state.reward;
						}
						clients [i].PushAgentState (agents [i].state);
					}
					Time.timeScale = 0.0f;
				}
			}
		}

        [SerializeField]
        GameObject _viewPrefab;

        [SerializeField]
        GameObject _canvas;

	}
}
