Feature: File Upload and Download

Background:
  Given the file upload service is running

Scenario: Uploading a valid PDF file returns a reference ID
  Given a valid PDF file named "report.pdf" of size 1024 bytes
  When I upload the file
  Then the response should be 201 Created
  And the response should contain a reference_id

Scenario: Downloading an existing file returns the file content
  Given a file has been uploaded with reference ID "FILE-20260516-Harsh1"
  When I download the file with reference ID "FILE-20260516-Harsh1"
  Then the response should be 200 OK
  And the response should have a Content-Disposition attachment header

Scenario: Downloading a non-existent file returns 404
  Given no file exists with reference ID "FILE-NOTFOUND-000"
  When I download the file with reference ID "FILE-NOTFOUND-000"
  Then the response status should be 404

Scenario: Getting metadata for an existing file returns structured JSON
  Given a file has been uploaded with reference ID "FILE-META-001"
  When I get metadata for reference ID "FILE-META-001"
  Then the response should be 200 OK
  And the metadata should contain a referenceId field

Scenario: Getting metadata for a non-existent file returns 404
  Given no file exists with reference ID "FILE-META-NOTFOUND"
  When I get metadata for reference ID "FILE-META-NOTFOUND"
  Then the response status should be 404

Scenario: Upload service throws returns 500
  Given the file service is unavailable
  When I download the file with reference ID "FILE-ERR-001"
  Then the response status should be 500
