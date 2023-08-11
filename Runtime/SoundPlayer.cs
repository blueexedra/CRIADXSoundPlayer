using UnityEngine;
using UnityEngine.Assertions;
using System.Linq;
using DG.Tweening;
using CriWare;
using System.IO;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace CRIADXSoundPlayer
{
    public class SoundPlayer : SingletonMonoBehaviour<SoundPlayer>
    {
        public readonly struct BGMPlaybackData
        {
            public CriAtomExPlayback PlayBack { get; }
            public Tweener Tweener { get; }
            public CriAtomSource Source { get; }

            public string CueName { get; }

            public BGMPlaybackData(CriAtomExPlayback playBack, Tweener tweener, CriAtomSource source, string cueName)
            {
                PlayBack = playBack;
                Tweener = tweener;
                Source = source;
                CueName = cueName;
            }
        }

        public readonly struct BGMStopData
        {
            public Tweener Tweener { get; }
            public CriAtomSource Source { get; }

            public BGMStopData(Tweener tweener, CriAtomSource source)
            {
                Tweener = tweener;
                Source = source;
            }
        }

        // クロスフェードとポーズのために3つインスタンスを用意
        private CriAtomSource[] _bgmSources = default;
        private CriAtomSource _seSource = default;

        private readonly IDictionary<string, string> _cueSheetNameByCueName = new Dictionary<string, string>();

        // これから再生停止されるBGMのデータ
        private BGMStopData _dataForStop = default;
        // これから再生されるBGMのデータ
        private BGMPlaybackData _dataForPlay = default;

        private static readonly string _aisacSoundControlName = "AisacControl_00";

        private string _currentAISACControlName = default;
        private bool _initialized = false;
        public bool Initialized => _initialized;

        public string CurrentPlayingCueName => _dataForPlay.CueName;

        protected override void Init()
        {
            _bgmSources = new CriAtomSource[]
            {
                 gameObject.AddComponent<CriAtomSource>(),
                 gameObject.AddComponent<CriAtomSource>(),
                 gameObject.AddComponent<CriAtomSource>(),
            };

            for (var i = 0; i < _bgmSources.Length; ++i)
            {
                _bgmSources[i].use3dPositioning = false;
                _bgmSources[i].SetAisacControl(_aisacSoundControlName, 0f);
            }

            _seSource = gameObject.AddComponent<CriAtomSource>();
            _seSource.use3dPositioning = false;
            InitCueSheetDictionary().Forget();
        }

        private async UniTask InitCueSheetDictionary()
        {
            var streamingAssetsInfo = new DirectoryInfo(Application.streamingAssetsPath);
            if (!streamingAssetsInfo.Exists) return;

            await UniTask.WaitWhile(() => CriAtom.CueSheetsAreLoading);

            var sheetNames = streamingAssetsInfo
                .EnumerateFiles("*.acb", SearchOption.AllDirectories)
                .Select(file => Path.GetFileNameWithoutExtension(file.Name))
                .Select(name => new { CueSheet = name, CueList = CriAtom.GetAcb(name).GetCueInfoList() });
            foreach (var c in sheetNames)
            {
                foreach (var cue in c.CueList)
                {
                    _cueSheetNameByCueName[cue.name] = c.CueSheet;
                }
            }
            _initialized = true;
        }

        private void SetBGMCueSheet(string cueSheetName)
        {
            for (var i = 0; i < _bgmSources.Length; ++i)
            {
                _bgmSources[i].cueSheet = cueSheetName;
            }
        }

        private void SetSECueSheet(string cueSheetName)
        {
            _seSource.cueSheet = cueSheetName;
        }

        private string GetCueSheetName(string cueName)
        {
            return _cueSheetNameByCueName[cueName].ToString();
        }

        public CriAtomExPlayback PlayBGM(string cueName, CriAtomSource sourceEmpty, float volume = 1f)
        {
            Assert.IsFalse(string.IsNullOrEmpty(cueName) || sourceEmpty == null || volume < 0f);

            sourceEmpty.volume = volume;

            var cueSheetName = GetCueSheetName(cueName);
            if (sourceEmpty.cueSheet != cueSheetName)
                SetBGMCueSheet(cueSheetName);

            return sourceEmpty.Play(cueName);
        }

        public CriAtomExPlayback PlayBGM(string cueName, string controlName, CriAtomSource sourceEmpty, float value = 1f, float volume = 1f)
        {
            Assert.IsFalse(string.IsNullOrEmpty(cueName) || string.IsNullOrEmpty(controlName) || sourceEmpty == null || value < 0f || 1f < value);

            sourceEmpty.volume = volume;

            var cueSheetName = GetCueSheetName(cueName);
            if (sourceEmpty.cueSheet != cueSheetName)
                SetBGMCueSheet(cueSheetName);

            sourceEmpty.SetAisacControl(controlName, value);
            _currentAISACControlName = controlName;

            return sourceEmpty.Play(cueName);
        }

        public CriAtomExPlayback PlaySE(string cueName, float volume = 1f)
        {
            Assert.IsFalse(string.IsNullOrEmpty(cueName) || volume < 0f || _seSource == null);
            _seSource.volume = volume;

            var cueSheetName = GetCueSheetName(cueName);
            if (_seSource.cueSheet != cueSheetName)
                SetSECueSheet(cueSheetName);

            return _seSource.Play(cueName);
        }

        public void StopSE()
        {
            _seSource.Stop();
        }

        public void StopBGMImmediately(CriAtomSource sourcePlaying = null)
        {
            var sourcePlaying_ = sourcePlaying == null
                ? _bgmSources.FirstOrDefault(src => src.status == CriAtomSource.Status.Playing)
                : sourcePlaying;
            sourcePlaying_.Stop();
        }

        public void StopAISACControlledSound(string controlName, CriAtomSource sourcePlaying = null)
        {
            var sourcePlaying_ = sourcePlaying == null
                ? _bgmSources.FirstOrDefault(src => src.status == CriAtomSource.Status.Playing)
                : sourcePlaying;
            sourcePlaying_.SetAisacControl(controlName, 0f);
            _currentAISACControlName = null;
        }

        public void ResetAllSources()
        {
            StopWithFadeOut();
            foreach (var s in _bgmSources)
            {
                s.volume = 0f;
            }
        }

        /// <summary>
        /// BGMをフェードイン付きで再生する
        /// </summary>
        /// <param name="cueName"></param>
        /// <param name="fadeTime"></param>
        public BGMPlaybackData PlayWithFadeIn(string cueName, CriAtomSource sourceEmpty = null, TweenCallback callback = null, float fadeTime = 0.2f)
        {
            var source = sourceEmpty == null
                ? _bgmSources.FirstOrDefault(src =>
                {
                    // CriAtomSourceはStop()を呼んでも即StatusがStopにならないため、ボリュームでも判断
                    return (src.status == CriAtomSource.Status.Stop || Mathf.Approximately(src.volume, 0f)) && !src.IsPaused();
                })
                : sourceEmpty;

            var playBack = PlayBGM(cueName, source, 0f);
            Tweener tweener = source
                .DOFade(1.0f, fadeTime)
                .OnComplete(callback)
                .SetAutoKill(false);

            return new BGMPlaybackData(playBack, tweener, source, cueName);
        }

        public BGMPlaybackData PlayWithFadeIn(string cueName, string controlName, CriAtomSource sourceEmpty = null, TweenCallback callback = null, float fadeTime = 0.2f)
        {
            Assert.IsFalse(string.IsNullOrEmpty(cueName) || string.IsNullOrEmpty(controlName));

            var source = sourceEmpty == null
                ? _bgmSources.FirstOrDefault(src =>
                {
                    // CriAtomSourceはStop()を呼んでも即StatusがStopにならないため、ボリュームでも判断
                    return (src.status == CriAtomSource.Status.Stop || Mathf.Approximately(src.volume, 0f)) && !src.IsPaused();
                })
                : sourceEmpty;

            var playBack = PlayBGM(cueName, controlName, source, 0f, 0f);
            Tweener tweener = DOVirtual.Float(0f, 1f, fadeTime, value => { source.SetAisacControl(controlName, value); source.volume = value; })
            .OnComplete(callback)
            .SetAutoKill(false)
            .SetLink(gameObject);

            return new BGMPlaybackData(playBack, tweener, source, cueName);
        }

        private BGMStopData Stop(CriAtomSource sourcePlaying, string controlName, TweenCallback callback = default, float fadeTime = 0.2f)
        {
            Assert.IsFalse(string.IsNullOrEmpty(controlName));

            Tweener tweener = DOVirtual
                    .Float(1.0f, 0.0f, fadeTime, value => { sourcePlaying.SetAisacControl(controlName, value); })
                    .OnComplete(() => { StopAISACControlledSound(controlName, sourcePlaying); callback?.Invoke(); })
                    .SetAutoKill(false);
            return new BGMStopData(tweener, sourcePlaying);
        }

        private BGMStopData Stop(CriAtomSource sourcePlaying, TweenCallback callback = default, float fadeTime = 0.2f, bool shouldStopAISAC = true)
        {
            Assert.IsFalse(sourcePlaying == null);

            Tweener tweener = sourcePlaying
            .DOFade(0.0f, fadeTime)
            .OnComplete(() =>
            {
                StopBGMImmediately(sourcePlaying);
                if (_currentAISACControlName != null && shouldStopAISAC)
                {
                    StopAISACControlledSound(_currentAISACControlName, sourcePlaying);
                }
                callback?.Invoke();
            })
            .SetAutoKill(false);

            return new BGMStopData(tweener, sourcePlaying);
        }

        /// <summary>
        /// BGMをフェードアウト付きで再生停止する
        /// </summary>
        /// <param name="fadeTime"></param>
        public BGMStopData StopWithFadeOut(string controlName, CriAtomSource source = null, TweenCallback callback = null, float fadeTime = 0.2f)
        {
            Assert.IsFalse(string.IsNullOrEmpty(controlName));

            // Volumeが0のSourceを選択しないように
            var sourcePlaying = source == null
                ? _bgmSources.FirstOrDefault(src => ShouldStop(src))
                : source;
            if (sourcePlaying != null)
            {
                return Stop(sourcePlaying, controlName, callback, fadeTime);
            }
            return default;
        }

        /// <summary>
        /// BGMをフェードアウト付きで再生停止する
        /// </summary>
        /// <param name="source"></param>
        /// <param name="callback"></param>
        /// <param name="fadeTime"></param>
        /// <returns></returns>
        public BGMStopData StopWithFadeOut(CriAtomSource source = null, TweenCallback callback = null, float fadeTime = 0.2f, bool shouldStopAISAC = true)
        {
            // Volumeが0のSourceを選択しないように
            var sourcePlaying = source == null
                ? _bgmSources.FirstOrDefault(src => ShouldStop(src))
                : source;
            if (sourcePlaying != null)
            {
                return Stop(sourcePlaying, callback, fadeTime, shouldStopAISAC);
            }
            return default;
        }

        private bool ShouldStop(CriAtomSource src)
        {
            return src.status == CriAtomSource.Status.Playing && Mathf.Approximately(src.volume, 1.0f) && !src.IsPaused();
        }

        private BGMPlaybackData PlayAISACControlledSound(string controlName, CriAtomSource sourcePlaying, float fadeTime = 0.2f, TweenCallback callback = null)
        {
            sourcePlaying.SetAisacControl(controlName, 0f);
            _currentAISACControlName = controlName;
            var tweener = DOVirtual.Float(0f, 1f, fadeTime, value => { sourcePlaying.SetAisacControl(controlName, value); })
                .OnComplete(callback)
                .SetAutoKill(false)
                .SetLink(gameObject);

            return new BGMPlaybackData(_dataForPlay.PlayBack, tweener, sourcePlaying, _dataForPlay.CueName);
        }

        private void OnSameBGM(string controlName)
        {
            if (string.IsNullOrEmpty(controlName) && !string.IsNullOrEmpty(_currentAISACControlName))
            {
                StopWithFadeOut(controlName: _currentAISACControlName);
            }
            else if (!string.IsNullOrEmpty(controlName))
            {
                _dataForPlay = PlayAISACControlledSound(controlName, _dataForPlay.Source);
            }
        }

        /// <summary>
        /// BGMをクロスフェードで再生する
        /// </summary>
        /// <param name="cueName"></param>
        /// <param name="fadeTimeStop"></param>
        /// <param name="fadeTimeForPlay"></param>
        /// <returns></returns>
        public void PlayWithCrossFade(string cueName, string controlName = default, float fadeTimeStop = 0.2f, float fadeTimeForPlay = 0.2f, bool pausedSourceExists = false, bool shouldStopAISAC = true)
        {
            Assert.IsFalse(string.IsNullOrEmpty(cueName));

            // 同じ曲でクロスフェードはしない
            if (cueName == _dataForPlay.CueName)
            {
                OnSameBGM(controlName);
                return;
            }

            if (!pausedSourceExists) StopPause();

            var isPlayTweenPlaying = _dataForPlay.Tweener != null && _dataForPlay.Tweener.IsPlaying();
            var isStopTweenPlaying = _dataForStop.Tweener != null && _dataForStop.Tweener.IsPlaying();

            // フェード処理に割り込む場合
            if (isPlayTweenPlaying || isStopTweenPlaying)
            {
                if (string.IsNullOrEmpty(controlName))
                {
                    CompleteInFade(cueName, fadeTimeStop, fadeTimeForPlay);
                }
                else
                {
                    CompleteInFade(cueName, controlName, fadeTimeStop, fadeTimeForPlay);
                }
            }
            else
            {
                if (string.IsNullOrEmpty(controlName))
                {
                    CrossFade(cueName, fadeTimeStop, fadeTimeForPlay, shouldStopAISAC);
                }
                else
                {
                    CrossFade(cueName, controlName, fadeTimeStop, fadeTimeForPlay);
                }
            }
        }

        public void PauseAndPlay(string cueName, float fadeTimeStop = 0.2f, float fadeTimeForPlay = 0.2f)
        {
            Assert.IsFalse(string.IsNullOrEmpty(cueName));

            // 同じ曲でクロスフェードはしない
            if (cueName == _dataForPlay.CueName)
            {
                return;
            }

            var isPlayTweenPlaying = _dataForPlay.Tweener != null && _dataForPlay.Tweener.IsPlaying();
            var isStopTweenPlaying = _dataForStop.Tweener != null && _dataForStop.Tweener.IsPlaying();

            // フェード処理に割り込む場合
            if (isPlayTweenPlaying || isStopTweenPlaying)
            {
                // 現在のフェード処理をすべて完了させる
                StopAllFade();

                var previousData = _dataForStop;
                _dataForStop = PauseWithFadeOut(_dataForPlay.Source, fadeTimeStop);
                _dataForPlay = PlayWithFadeIn(cueName, previousData.Source, null, fadeTimeForPlay);
            }
            else
            {
                _dataForStop = PauseWithFadeOut(fadeTime: fadeTimeStop);
                _dataForPlay = PlayWithFadeIn(cueName, fadeTime: fadeTimeForPlay);
            }
        }

        public void StopAndResume(string resumedSoundCueName, float fadeTimeStop = 0.2f, float fadeTimeForPlay = 0.2f)
        {
            _dataForStop = StopWithFadeOut(fadeTime: fadeTimeStop);
            _dataForPlay = ResumeWithFadeIn(resumedSoundCueName, fadeTimeForPlay);
        }

        private void CrossFade(string cueName, string controlName, float fadeTimeStop = 0.2f, float fadeTimeForPlay = 0.2f)
        {
            // AISACを止めない
            _dataForStop = StopWithFadeOut(fadeTime: fadeTimeStop, shouldStopAISAC: false);
            _dataForPlay = PlayWithFadeIn(cueName, controlName, fadeTime: fadeTimeForPlay);
        }

        private void CrossFade(string cueName, float fadeTimeStop = 0.2f, float fadeTimeForPlay = 0.2f, bool shouldStopAISAC = true)
        {
            _dataForStop = StopWithFadeOut(fadeTime: fadeTimeStop, shouldStopAISAC: shouldStopAISAC);
            _dataForPlay = PlayWithFadeIn(cueName, fadeTime: fadeTimeForPlay);
        }

        private void CompleteInFade(string cueName, string controlName, float fadeTimeStop = 0.2f, float fadeTimeForPlay = 0.2f)
        {
            // 現在のフェード処理をすべて完了させる
            StopAllFade();

            var previousData = _dataForStop;
            _dataForStop = StopWithFadeOut(_dataForPlay.Source, null, fadeTimeStop);

            _dataForPlay = PlayWithFadeIn(cueName, controlName, previousData.Source, null, fadeTimeForPlay);
        }

        private void CompleteInFade(string cueName, float fadeTimeStop = 0.2f, float fadeTimeForPlay = 0.2f)
        {
            // 現在のフェード処理をすべて完了させる
            StopAllFade();

            var previousData = _dataForStop;
            _dataForStop = StopWithFadeOut(_dataForPlay.Source, null, fadeTimeStop);

            _dataForPlay = PlayWithFadeIn(cueName, previousData.Source, null, fadeTimeForPlay);
        }

        private void StopAllFade()
        {
            if (_dataForPlay.Tweener != null && _dataForPlay.Tweener.IsPlaying()) _dataForPlay.Tweener.Complete();
            if (_dataForStop.Tweener != null && _dataForStop.Tweener.IsPlaying()) _dataForStop.Tweener.Complete();
        }

        public void PauseImmediately(CriAtomSource sourcePlaying = null)
        {
            var sourcePlaying_ = sourcePlaying == null
                ? _bgmSources.FirstOrDefault(src => src.status == CriAtomSource.Status.Playing)
                : sourcePlaying;
            sourcePlaying.Pause(true);
        }

        public CriAtomSource StopPause(CriAtomSource sourcePaused = null)
        {
            var paused = sourcePaused == null ? _bgmSources
                .FirstOrDefault(src => src.status == CriAtomSource.Status.Playing && src.IsPaused())
                : sourcePaused;

            if (paused == null) return default;

            paused.Pause(false);
            paused.Stop();

            return paused;
        }

        public CriAtomSource ResumeImmediately()
        {
            var sourcePaused = _bgmSources
                .FirstOrDefault(src => src.status == CriAtomSource.Status.Playing && src.IsPaused());

            Assert.IsFalse(sourcePaused == null);

            sourcePaused.Pause(false);
            return sourcePaused;
        }

        public BGMStopData PauseWithFadeOut(CriAtomSource source = null, float fadeTime = 0.2f)
        {
            // Volumeが0のSourceを選択しないように
            var sourcePlaying = source == null
                ? _bgmSources.FirstOrDefault(src => src.status == CriAtomSource.Status.Playing && Mathf.Approximately(src.volume, 1.0f))
                : source;
            if (sourcePlaying != null)
            {
                return Pause(sourcePlaying, fadeTime);
            }
            return default;
        }

        public BGMPlaybackData ResumeWithFadeIn(string resumedSoundCueName, float fadeTime = 0.2f)
        {
            return Resume(resumedSoundCueName, fadeTime);
        }

        private BGMStopData Pause(CriAtomSource source = null, float fadeTime = 0.2f)
        {
            var sourcePlaying = source == null ? _bgmSources.FirstOrDefault(src => src.status == CriAtomSource.Status.Playing) : source;
            var tweener = sourcePlaying
                .DOFade(0.0f, fadeTime)
                .OnComplete(() => PauseImmediately(sourcePlaying))
                .SetAutoKill(false);

            return new BGMStopData(tweener, sourcePlaying);
        }

        private BGMPlaybackData Resume(string resumedSoundCueName, float fadeTime = 0.2f)
        {
            var source = ResumeImmediately();
            var tweener = source
                .DOFade(1.0f, fadeTime)
                .SetAutoKill(false);

            return new BGMPlaybackData(default, tweener, source, resumedSoundCueName);
        }
    }
}