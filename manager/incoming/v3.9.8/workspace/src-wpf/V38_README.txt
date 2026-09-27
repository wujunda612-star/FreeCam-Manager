FreeCam Manager V3.9

核心变化：
- 版本库主存储从 library.json 迁移为 SQLite（library.db）。
- 首次启动自动从 V3.7 library.json / .bak 安全迁移，原 JSON 不删除。
- SQLite 使用事务 + WAL，并保留最近 10 代已校验数据库备份。
- 持续生成 SQLiteRecovery/library-latest.json 灾难恢复导出。
- 主库损坏时按 SQLite 主库 -> SQLite 备份 -> Recovery JSON -> 旧 JSON -> 新建空库逐级恢复。
- 修复旧路径重定位发生重复目标时的 UNIQUE(path) 冲突：安全合并元数据后只保留一条记录。
- 保留 V3.7 Fix1 原有 Watcher / Organizer / Stable / 01_Testing / Result / 自动更新工作流。
- 正式运行文件仍为 FreeCam_Manager.exe。

回退：
源码包含 FreeCamManager.V38Rollback，可将当前 SQLite 快照原子导出回 V3.7 library.json。
