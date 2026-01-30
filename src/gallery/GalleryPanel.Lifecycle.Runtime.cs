using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Events;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        public void SetStatus(string msg)
        {
            if (string.IsNullOrEmpty(msg)) dragStatusMsg = null;
            else dragStatusMsg = msg;
        }

        public void ShowTemporaryStatus(string msg, float duration = 2.0f)
        {
            temporaryStatusMsg = msg;
            if (temporaryStatusCoroutine != null) StopCoroutine(temporaryStatusCoroutine);
            temporaryStatusCoroutine = StartCoroutine(ClearTemporaryStatus(duration));
        }

        private IEnumerator ClearTemporaryStatus(float duration)
        {
            yield return new WaitForSeconds(duration);
            temporaryStatusMsg = null;
            temporaryStatusCoroutine = null;
        }
    }

}
