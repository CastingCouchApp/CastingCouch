# Creator Control Suite 2.0.148

## Workflow API compile fix
- Replaced the invalid `IStreamWorkflowService.GetState()` call with the actual `IStreamWorkflowService.State` property.
- Preserves the dashboard automation summary introduced in 2.0.144.
- Preserves all action-safety and service-toggle changes through 2.0.147.
