using UnityEngine;

namespace CRIADXSoundPlayer
{
    public abstract class SingletonMonoBehaviour<T> : MonoBehaviour where T : MonoBehaviour
    {
        private static T _instance = null;

        public static T Instance
        {
            get
            {
                if (_instance != null) return _instance;

                InitInstance(shouldInit: true);
                return _instance;
            }
        }

        private static void InitInstance(bool shouldInit)
        {
            var typeOfThis = typeof(T);
            _instance = FindObjectOfType<T>();

            if (_instance == null)
            {
                var go = new GameObject(typeOfThis.Name);
                _instance = go.AddComponent<T>();
                DontDestroyOnLoad(go);
            }

            if (shouldInit)
            {
                (_instance as SingletonMonoBehaviour<T>).Init();
            }
        }

        public void Awake()
        {
            InitInstance(shouldInit: false);
        }

        protected abstract void Init();
    }
}