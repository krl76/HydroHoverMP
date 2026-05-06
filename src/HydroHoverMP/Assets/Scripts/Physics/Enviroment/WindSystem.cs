using UnityEngine;

namespace Physics.Enviroment
{
    public class WindSystem : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("Направление ветра (нормализуется автоматически).")]
        [SerializeField] private Vector3 _direction = new Vector3(1, 0, 0);
    
        [Tooltip("Сила ветра в м/с.")]
        [SerializeField] private float _strength = 10f;
    
        public Vector3 WindVector => _direction.normalized * _strength;

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            Vector3 center = transform.position;
            Vector3 arrowTip = center + WindVector;
        
            Gizmos.DrawLine(center, arrowTip);
            Gizmos.DrawSphere(arrowTip, 0.5f);
        }
    }
}