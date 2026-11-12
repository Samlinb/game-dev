using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiniGame.Data
{
    /// <summary>
    /// 广告位配置:业务只认场景名(placement),各平台广告位 ID 在这里填。
    /// 平台字段留空 = 该平台此场景位没配(AdsManager 真机上会报错并按失败回调)。
    /// </summary>
    [CreateAssetMenu(menuName = "MiniGame/AdConfig")]
    public class AdConfig : ScriptableObject
    {
        public List<Placement> placements = new List<Placement>();

        public string GetAdUnitId(string platformName, string placement)
        {
            var entry = placements.Find(p => p.placement == placement);
            if (entry == null) return null;

            if (platformName.Contains("WeChat")) return entry.wechatAdUnitId;
            if (platformName.Contains("Douyin")) return entry.douyinAdUnitId;

            // 编辑器/未知平台:随便给一个非空的,方便编辑器跑通全流程
            return string.IsNullOrEmpty(entry.wechatAdUnitId) ? entry.douyinAdUnitId : entry.wechatAdUnitId;
        }

        [Serializable]
        public class Placement
        {
            public string placement; // 业务场景名,如 double_coins(翻倍)、revive(复活)
            public string wechatAdUnitId;
            public string douyinAdUnitId;
        }
    }
}
