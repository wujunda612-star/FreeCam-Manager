# FreeCam 可视化管理器 V3.8

V3.8 将 Manager 的版本库主存储从 `library.json` 迁移到 SQLite，并保留现有文件工作流与软件更新机制。

- 首次启动会从 V3.7 的 `library.json` / `.bak` 安全迁移到 `library.db`，旧 JSON 不会被删除。
- SQLite 写入使用事务 / WAL；保留最近 10 代已校验数据库备份。
- 持续导出 `SQLiteRecovery/library-latest.json` 作为独立灾难恢复副本。
- 主数据库异常时按 SQLite 备份 → Recovery JSON → 旧 JSON 的顺序恢复；所有来源都不可用时仍可创建可写新库并继续启动。
- 提供显式 V3.7 回退工具，可将最新 SQLite 状态原子写回 legacy JSON；回退后重新进入 V3.8 会以最新 legacy 数据重新迁移，避免旧 SQLite 代际复活。
- 保留 Watcher、Organizer、Stable、`01_Testing`、Result 关联、Archive、版本库重建及 Manager 自动更新工作流。
- 修复旧路径重定位时多条索引记录合流到同一物理文件导致的 SQLite `UNIQUE(path)` 冲突；碰撞时安全合并人工结论、星级、备注、锁定、测试状态等元数据。
- SQLite 架构已完成故障注入、600 轮耐久压力、真实 JSON 迁移、WPF 重启持久化、完整文件工作流沙盒回归及 RC 实机验证。
