/* NativeAOT emits RhDebugBreak, but Cosmos 3.0.88 does not provide the runtime symbol. */
void RhDebugBreak(void)
{
    __builtin_debugtrap();
}
