# File Upload Microservice

## Overview

The File Upload Microservice is a centralized, production-style ASP.NET Core microservice designed for secure file uploads, video uploads, metadata extraction, virus scanning, and reusable storage management.

The service acts as a dedicated centralized file platform that can later be consumed by multiple business microservices such as:

* VCIP systems
* KYC systems
* document services
* employee portals
* media services

Instead of allowing every business service to manage its own storage logic, this microservice centralizes:

* file storage
* metadata management
* validation
* virus scanning
* video processing
* retrieval

This creates a cleaner and more scalable microservice architecture.

---

# Project Goals

The primary goals of this project are:

* centralized file storage architecture
* reusable upload APIs
* secure upload handling
* video metadata extraction
* virus scanning support
* clean layered architecture
* repository pattern implementation
* storage abstraction
* production-style design
* beginner-friendly scalability

The project intentionally avoids unnecessary enterprise complexity in the current phase.

---

# Current Features

## Generic File Upload

Supports uploading:

* videos
* images
* PDFs
* DOC/DOCX files
* text files
* binary files

The system uses a single centralized upload endpoint for all file types.

---

## Video Upload Support

Supports:

* MP4 uploads
* H.264 video validation
* AAC audio validation

Video metadata extraction includes:

* duration
* resolution
* video codec
* audio codec
* file size

Metadata extraction is performed using:

* FFprobe

---

## Virus Scanning

Integrated with:

* ClamAV

All uploaded files are scanned before being accepted.

If malware is detected:

* upload is rejected
* file is not stored

---

## Metadata Persistence

Metadata is stored in PostgreSQL including:

* reference ID
* original filename
* storage path
* MIME type
* file size
* upload timestamps
* video metadata
* delete status

Actual files are NOT stored in PostgreSQL.

---

## Soft Delete Support

Supports logical deletion:

* file remains physically stored
* metadata remains in DB
* queries ignore deleted files

Soft delete uses:

* is_deleted
* deleted_at

---

## Correlation ID Middleware

Every request receives:

* X-Correlation-ID

Used for:

* request tracing
* debugging
* observability
* distributed tracing readiness

---

## Structured Logging

Project includes:

* request logging middleware
* exception middleware
* structured service logs

Logs include:

* uploads
* validation failures
* virus scan results
* metadata extraction
* deletions
* exceptions

---

# Architecture

The project follows a minimal production-style layered architecture.

```text id="h5ivgb"
Client
   ↓
Controller Layer
   ↓
Service Layer
   ↓
Repository Layer
   ↓
PostgreSQL

AND

Service Layer
   ↓
Storage Service
   ↓
Local File Storage
```

---

# Architectural Principles

The architecture focuses on:

* separation of concerns
* storage abstraction
* centralized storage management
* reusable service boundaries
* future scalability

Business services should:

* NEVER store files directly
* ONLY store FileId/referenceId

The upload service owns:

* storage
* retrieval
* validation
* metadata management

---

# Folder Structure

```text id="0aonv5"
FileUploadService/
│
├── Application/
│   ├── Configurations/
│   ├── DTOs/
│   ├── Implementation/
│   ├── Interfaces/
│   └── Models/
│
├── Controllers/
│
├── Infrastructure/
│
├── Middleware/
│
├── uploads/
│
├── Program.cs
├── appsettings.json
└── README.md
```

---

# Core Components

## FileService

Responsible for:

* upload orchestration
* validation coordination
* virus scan coordination
* storage coordination
* metadata persistence coordination

The FileService acts as:

* orchestration layer
* workflow coordinator

It does NOT directly own:

* SQL logic
* storage implementation details

---

## Repository Layer

Responsible for:

* PostgreSQL queries
* metadata persistence
* soft delete operations
* retrieval operations

Uses:

* Dapper

for lightweight high-performance database access.

---

## Storage Layer

Responsible for:

* physical file storage
* file retrieval
* file deletion

Current implementation:

* LocalStorageService

Future-ready for:

* MinIO
* S3-compatible storage
* cloud object storage

through:

* IStorageService abstraction

---

## Middleware Layer

Includes:

### CorrelationIdMiddleware

Adds request tracing support.

### RequestLoggingMiddleware

Logs incoming requests and responses.

### ExceptionMiddleware

Provides centralized exception handling.

---

# Upload Flow

```text id="4sz34o"
Client
 ↓
Controller
 ↓
Validation
 ↓
Virus Scan
 ↓
FFprobe Metadata Extraction
 ↓
Storage Service
 ↓
PostgreSQL Metadata Save
 ↓
Response with FileId
```

---

# Video Metadata Extraction

The service uses:

* FFprobe

for:

* codec detection
* duration extraction
* resolution extraction
* corruption checks

Supported current video workflow:

* upload MP4
* extract metadata
* persist metadata
* return reference ID

---

# Storage Strategy

Current storage:

* local filesystem

Example path:

```text id="40t9lj"
C:\Users\harsh.gupta\Documents\uploads
```

Architecture is intentionally prepared for future migration to:

* MinIO
* S3-compatible object storage
* cloud storage

without major rewrites.

---

# Database Design

## Main Metadata Table

### files

Stores:

* reference_id
* storage_path
* original_filename
* content_type
* file_size
* created_at
* is_deleted
* deleted_at

---

## Video Metadata Table

### video_metadata

Stores:

* video duration
* codecs
* resolution
* video-specific metadata

---

# Security Features

The project currently supports:

* MIME type validation
* extension validation
* file size validation
* virus scanning
* request size limits
* multipart upload limits

---

# Third-Party Dependencies & External Integrations

## PostgreSQL

Used for:

* metadata persistence
* soft delete tracking
* file reference management

Chosen because:

* production-grade reliability
* strong consistency
* excellent ASP.NET support

---

## Dapper

Used as lightweight ORM/data-access layer.

Chosen because:

* lightweight
* high performance
* simple SQL control

---

## ClamAV

Used for antivirus scanning.

Chosen because:

* open source
* production-proven
* commonly used in upload systems

---

## FFmpeg / FFprobe

Used for:

* video metadata extraction
* codec detection
* resolution extraction

Chosen because:

* industry standard
* open source
* supports almost every media format

---

## Swagger / OpenAPI

Used for:

* API documentation
* API testing
* endpoint exploration

---

## ASP.NET Core

Main backend framework.

Chosen because:

* high performance
* async-first architecture
* strong middleware support
* microservice-friendly design

---

## Microsoft ILogger

Used for:

* structured logging
* request tracing
* operational visibility

---

# Soft Delete & Hard Delete Design

## Soft Delete

Logical deletion only:

* metadata retained
* physical file retained

Used for:

* audit safety
* accidental deletion recovery

---

## Hard Delete (Planned)

Will:

* remove physical file
* permanently delete DB row

Reserved for:

* administrative cleanup
* retention-policy cleanup

---

# Intended Microservice Usage

This upload service is designed to be consumed by business microservices.

Example future architecture:

```text id="dfgbt5"
VCIP Service
   ↓
Upload Service
   ↓
Storage
```

Business services should store ONLY:

* FileId/referenceId

Actual files remain centralized inside the upload service.

---

# Future Improvements

Planned future enhancements:

* MinIO integration
* RabbitMQ integration
* API Gateway integration
* JWT authentication
* presigned upload URLs
* chunked uploads
* CDN integration
* Prometheus monitoring
* Grafana dashboards
* Fluent Bit centralized logging

---

# Current Scope Intentionally Avoids

The project intentionally avoids:

* RabbitMQ
* event-driven architecture
* distributed transactions
* Kubernetes complexity
* OAuth/JWT complexity
* CQRS overengineering

The current focus is:

* strong architectural fundamentals
* centralized upload-service design
* production-style simplicity

---

# Running The Project

## Requirements

* .NET 8 SDK
* PostgreSQL
* ClamAV
* FFmpeg/FFprobe

---

## Run Project

```bash id="o0yqjs"
dotnet run
```

---

# Swagger

Example:

```text id="e9mb0r"
http://localhost:5000
```

or

```text id="3l4i4p"
https://localhost:5001
```

depending on launch profile.

---

# Development Philosophy

This project focuses on:

* clean architecture
* production-style design
* incremental scalability
* centralized storage management
* realistic microservice patterns

while remaining:

* beginner-friendly
* extensible
* lightweight
* maintainable.
