public static class Define
{
	public const string resourceNamespace_MySQL = "Microsoft.DBforMySQL";
	public const string resourceNamespace_PostgreSQL = "Microsoft.DBforPostgreSQL";
	public const string metric_CPU_Percent = "cpu_percent";	
	public const string metric_Storage_Percent = "storage_percent";
	public const string metric_Storage_Used = "storage_used";
	public const string metric_Storage_Limit = "storage_limit";	
	public const string ApiVersion = "2017-12-01";
	public const string dbTier_basic = "Basic";
	public const string dbTier_generalPurpose = "GeneralPurpose";
	public const string dbTier_memoryOptimized = "MemoryOptimized";
	public static int[] vcore_scaleModel_basic = { 1, 2 };
	public static int[] vcore_scaleModel_generalPurpose = { 2, 4, 8, 16, 32 };
	public static int[] vcore_scaleModel_memoryOptimized = { 2, 4, 8, 16, 32 };
	public static int[] storageRange_basic = { 5, 1024 };  //GB
	public static int[] storageRange_generalPurpose = { 5, 2048 };  //GB
	public static int[] storageRange_memoryOptimized = { 5, 2048 };  //GB
}
