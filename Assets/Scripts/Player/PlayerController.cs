using System.Linq;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("���݂̈ʒu")]
    public MapNode currentNode;

    [Header("�ړ��ݒ�")]
    public float moveSpeed = 3f;
    public float waitTime = 0.3f;      // Node�Ԃ̈ړ��Ԋu
    public float explorationRate = 0.2f; // �����_���T���m��(20%)

    private bool isMoving = false;

    void Update()
    {
        if (!isMoving && currentNode != null)
        {
            StartCoroutine(MoveNextNode());
        }
    }

    private System.Collections.IEnumerator MoveNextNode()
    {
        isMoving = true;

        if (currentNode.links.Count > 0)
        {
            // 1 ���Node�̒��ōł�Value���������̂��擾
            MapNode nextNode = currentNode.links
                .OrderByDescending(n => n.value)
                .First();

            // 2 �m���I�Ƀ����_���ړ�(����)
            if (Random.value < explorationRate)
            {
                nextNode = currentNode.links[Random.Range(0, currentNode.links.Count)];
            }

            // 3 �ړ�
            yield return StartCoroutine(MoveTo(nextNode.transform.position));

            // 4 ���݈ʒu�X�V
            currentNode = nextNode;
        }

        // 5 �����ҋ@���Ď��̈ړ���
        yield return new WaitForSeconds(waitTime);
        isMoving = false;
    }

    private System.Collections.IEnumerator MoveTo(Vector3 target)
    {
        while (Vector3.Distance(transform.position, target) > 0.01f)
        {
            transform.position = Vector3.MoveTowards(transform.position, target, moveSpeed * Time.deltaTime);
            yield return null;
        }
        transform.position = target;
    }
}
