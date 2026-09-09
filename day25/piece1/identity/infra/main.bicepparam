using 'main.bicep'

// None of these values are secrets: a UPN, three GUIDs, and an api:// URI are public
// identifiers, not credentials. No password, connection string, or key ever appears here.
param aadAdminLogin = 'shubh.rastogi2@s.amity.edu'
param aadAdminObjectId = '02d07c6e-13c4-4036-9eb6-c347193ab404'

param entraTenantId = '8d46a076-d093-416d-a57b-8692cde13bf8'
param entraClientId = '3a8d11f8-0721-4cc5-88db-a138235eb472'
param entraAudience = 'api://3a8d11f8-0721-4cc5-88db-a138235eb472'
