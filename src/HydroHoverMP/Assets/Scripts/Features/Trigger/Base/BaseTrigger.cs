using UnityEngine;

namespace Features.Trigger.Base
{
    public abstract class BaseTrigger : MonoBehaviour
    {
        private void OnTriggerEnter(Collider other)
        {
            if (other.gameObject.CompareTag("Player"))
            {
                OnPlayerEnter(other);
            }
        }
        
        private void OnTriggerStay(Collider other)
        {
            if (other.gameObject.CompareTag("Player"))
            {
                OnPlayerStay(other);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.gameObject.CompareTag("Player"))
            {
                OnPlayerExit(other);
            }
        }

        public abstract void OnPlayerEnter(Collider other);
        public abstract void OnPlayerStay(Collider other);
        public abstract void OnPlayerExit(Collider other);
    }
}