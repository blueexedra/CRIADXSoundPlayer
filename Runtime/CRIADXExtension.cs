// Ignore Spelling: Tweening

using UnityEngine;
using DG.Tweening.Core;
using DG.Tweening.Plugins.Options;
using CriWare;

namespace DG.Tweening
{
    public static class CriADX2Extensions
    {
        public static TweenerCore<float, float, FloatOptions> DOFade(this CriAtomSource atomSource, float to, float duration)
        {
            var endValueClamped = Mathf.Clamp(to, 0.0f, 1.0f);
            var tweener = DOTween.To(() => atomSource.volume, val => atomSource.volume = val, endValueClamped, duration);
            tweener.SetTarget(atomSource);
            return tweener;
        }
    }
}