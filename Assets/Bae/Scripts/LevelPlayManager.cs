using UnityEngine;
using Unity.Services.LevelPlay;

namespace Bae
{
    public class LevelPlayManager : MonoBehaviour
    {
        [Header("LevelPlay App Keys (대시보드 발급)")]
        public string androidAppKey = "YOUR_ANDROID_APP_KEY";
        public string iosAppKey = "YOUR_IOS_APP_KEY";
        
        [Header("Rewarded Ad Unit ID (광고 단위 ID)")]
        [Tooltip("보상형 광고의 Ad Unit ID를 입력하세요.")]
        public string rewardedAdUnitId = "YOUR_AD_UNIT_ID";

        // 최신 LevelPlay 보상형 광고 객체
        private LevelPlayRewardedAd rewardedAd;

        private void Start()
        {
            string appKey = "";
#if UNITY_ANDROID
            appKey = androidAppKey;
#elif UNITY_IOS
            appKey = iosAppKey;
#endif
            if (string.IsNullOrEmpty(appKey) || appKey.Contains("YOUR_"))
            {
                Debug.LogWarning("LevelPlayManager: App Key가 설정되지 않았습니다.");
            }

            // 1. SDK 초기화 이벤트 등록
            LevelPlay.OnInitSuccess += OnInitSuccess;
            LevelPlay.OnInitFailed += OnInitFailed;

            // 2. SDK 초기화 시작
            Debug.Log($"LevelPlay SDK 초기화 시작 (AppKey: {appKey})");
            LevelPlay.Init(appKey);
        }

        // SDK 초기화 성공 시
        private void OnInitSuccess(LevelPlayConfiguration config)
        {
            Debug.Log("LevelPlay SDK 초기화 성공!");
            
            // 보상형 광고 인스턴스 생성
            rewardedAd = new LevelPlayRewardedAd(rewardedAdUnitId);
            
            // 광고 이벤트 연결
            rewardedAd.OnAdLoaded += RewardedVideoAdLoadedEvent;
            rewardedAd.OnAdLoadFailed += RewardedVideoAdLoadFailedEvent;
            rewardedAd.OnAdRewarded += RewardedVideoAdRewardedEvent;
            rewardedAd.OnAdClosed += RewardedVideoAdClosedEvent;

            // 광고 수동 로드 (처음 1회 로드)
            rewardedAd.LoadAd();
        }

        // SDK 초기화 실패 시
        private void OnInitFailed(LevelPlayInitError error)
        {
            Debug.LogError($"LevelPlay SDK 초기화 실패: {error}");
        }

        // 유저가 "배속 버튼"을 눌렀을 때 호출 (UI Button OnClick 연결)
        public void ShowSpeedBuffAd()
        {
            if (rewardedAd != null && rewardedAd.IsAdReady())
            {
                Debug.Log("보상형 광고를 재생합니다.");
                rewardedAd.ShowAd();
            }
            else
            {
                Debug.LogWarning("광고가 아직 준비되지 않았습니다. 잠시 후 다시 시도해주세요.");
                rewardedAd?.LoadAd();
            }
        }

        // [핵심] 광고를 끝까지 시청해서 보상을 줘야 할 때
        private void RewardedVideoAdRewardedEvent(LevelPlayAdInfo adInfo, LevelPlayReward reward)
        {
            Debug.Log("보상형 광고 시청 완료! 60초 동안 2배속 버프를 지급합니다!");
            SpeedBuffManager.Instance.ActivateSpeedBuff(60f, 2f);
        }

        // 광고 창이 닫혔을 때 (다음을 위해 다시 로드)
        private void RewardedVideoAdClosedEvent(LevelPlayAdInfo adInfo)
        {
            Debug.Log("광고 창 닫힘. 다음 광고 시청을 위해 미리 로드합니다.");
            rewardedAd?.LoadAd();
        }

        private void RewardedVideoAdLoadedEvent(LevelPlayAdInfo adInfo)
        {
            Debug.Log("보상형 광고가 성공적으로 로드되었습니다.");
        }

        private void RewardedVideoAdLoadFailedEvent(LevelPlayAdError error)
        {
            Debug.LogError($"보상형 광고 로드 실패: {error}");
        }

        private void OnDestroy()
        {
            // 메모리 누수 방지 이벤트 해제
            LevelPlay.OnInitSuccess -= OnInitSuccess;
            LevelPlay.OnInitFailed -= OnInitFailed;

            if (rewardedAd != null)
            {
                rewardedAd.OnAdLoaded -= RewardedVideoAdLoadedEvent;
                rewardedAd.OnAdLoadFailed -= RewardedVideoAdLoadFailedEvent;
                rewardedAd.OnAdRewarded -= RewardedVideoAdRewardedEvent;
                rewardedAd.OnAdClosed -= RewardedVideoAdClosedEvent;
                
                rewardedAd.DestroyAd(); // 객체 정리
            }
        }
    }
}
