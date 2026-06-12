namespace UnluckSoftware
{
	// Demo script to preview gameobjects and particle systems
	// Please ignore the MinMaxCurve error, it is harmless.
	// !Gizmos must be enabled for this object.

	using UnityEngine;
	using UnityEditor;

	public class EnableSelectedGameObject :MonoBehaviour
	{
		[HideInInspector]
		public Transform prevSelect;
		[HideInInspector]
		public Transform prevSelectWrong;
		[Header("! Gizmos must be enabled.")]
		public int particleSystems;
		public bool disableMe;
		public string ignoreTag = "^^^^";
		public bool disableOnPlay;
		public bool autoSlideShow;
		public float autoSlideShowDelay = 3f;
		public string nextKey = "n";
		public bool randomEnable = false;
		public bool repeat;
		int emitOnEnable = 1;       // Emit particles instantly when enabling the ganeobject
		int counter = 0;
		Transform randomEnablePlaceHolder;
		GameObject prevRandomBird;
		bool autoSlideShowStarted;
		bool selfSelected;

		void Start()
		{
			if (disableOnPlay)
			{
				gameObject.SetActive(false);
				return;
			}

			if (randomEnable)
			{
				DisableAllChildren();
				randomEnablePlaceHolder = new GameObject("randomEnablePlaceHolder").transform;
			}

			if (autoSlideShow)
			{
				DisableAllChildren();
			}
		}

		private void Update()
		{
			if (autoSlideShowStarted) return;
			if (Input.GetKeyUp("n"))
			{
				if (autoSlideShow)
				{
					if (randomEnable) InvokeRepeating("RandomModel", 0, autoSlideShowDelay);
					else InvokeRepeating("NextModel", 0, autoSlideShowDelay);
					autoSlideShowStarted = true;
				} else
				{
					if (randomEnable) RandomModel();
					else NextModel();
				}
			}
		}

		void RandomModel()
		{
			if (transform.childCount == 0 && !repeat) return;

			if (transform.childCount == 0 && repeat)
			{
				while (randomEnablePlaceHolder.childCount > 0)
				{
					randomEnablePlaceHolder.GetChild(0).parent = transform;
				}
			}
			if (prevRandomBird != null)
			{
				prevRandomBird.SetActive(false);
				prevRandomBird.transform.SetParent(randomEnablePlaceHolder.transform);
			}
			if (transform.childCount == 0) return;
			int randomBird = Random.Range(0, transform.childCount);
			prevRandomBird = transform.GetChild(randomBird).gameObject;
			prevRandomBird.SetActive(true);
		}

		void PlayParticles()
		{
			if (!prevSelect) return;
			ParticleSystem pss;
			pss = prevSelect.GetComponent<ParticleSystem>();
			if (!pss) return;
			pss.Clear();
			pss.Play();
			pss.Emit(emitOnEnable);
		}

		void DisableAllChildren()
		{
			for (int i = 0; i < transform.childCount; i++)
			{
				Transform t = transform.GetChild(i).transform;
				if (t.gameObject.activeInHierarchy && t.parent == transform)
				{
					t.gameObject.SetActive(false);
				}
			}
		}

		void NextModel()
		{
			DisableAllChildren();
			transform.GetChild(counter % transform.childCount).gameObject.SetActive(true);
			counter++;
		}

#if UNITY_EDITOR
		void OnDrawGizmos()
		{
			if (disableMe) return;

			

			//if (Application.isPlaying) return;
			if (Selection.activeTransform == null) return;
			Transform s = Selection.activeTransform;
			if (s == prevSelect) return;
			if (s == transform)
			{
				if (selfSelected) return;
				selfSelected = true;
				DisableAllChildren();
				return;
			}
			if (s == prevSelect) return;
			if (s == prevSelectWrong) return;
			if (s.parent == null) return;
			if (s.name.Contains(ignoreTag)) return;
			if (s.parent != transform) return;
			if (prevSelect != null)
			{
				selfSelected = false;
				prevSelect.gameObject.SetActive(false);
				s.gameObject.SetActive(true);
				ParticleSystem pss = s.GetComponent<ParticleSystem>();
				if (pss)
				{
					pss.time -= pss.time;
					pss.Stop();
				}
			}
			prevSelect = s;
			CountParticles();
			PlayParticles();
			
				
		}
#endif

		void CountParticles()
		{
			ParticleSystem[] obj = transform.GetComponentsInChildren<ParticleSystem>(true);
			particleSystems = 0;
			for (int i = 0; i < obj.Length; i++)
			{
				if (obj[i].transform.parent != null)
				{
					ParticleSystem p = obj[i].transform.parent.GetComponent<ParticleSystem>();
					if (p == null)
					{
						particleSystems++;
					}
				}
			}
		}
	}
}
