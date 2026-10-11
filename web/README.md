# DeployVare - Frontend (Next.js)

## 🎨 Overview

Modern Next.js 14 frontend for DeployVare SQL Deployment Management System.

## 🚀 Tech Stack

- **Framework:** Next.js 14 (App Router)
- **Language:** TypeScript
- **Styling:** Tailwind CSS
- **State Management:** Zustand
- **HTTP Client:** Axios
- **Icons:** Lucide React
- **UI Components:** Custom components with Tailwind

## 📁 Project Structure

```
web/
├── app/                    # Next.js App Router
│   ├── dashboard/          # Dashboard page
│   ├── applications/       # Applications management (TO BE CREATED)
│   ├── instances/          # Database instances (TO BE CREATED)
│   ├── deploy/             # Deployment interface (TO BE CREATED)
│   ├── history/            # Deployment history (TO BE CREATED)
│   ├── compare/            # Schema comparison (TO BE CREATED)
│   ├── login/              # Login page ✅
│   ├── layout.tsx          # Root layout ✅
│   ├── page.tsx            # Home (redirects) ✅
│   └── globals.css         # Global styles ✅
├── components/
│   ├── ui/                 # Reusable UI components
│   │   ├── button.tsx      # ✅
│   │   ├── card.tsx        # ✅
│   │   ├── input.tsx       # ✅
│   │   ├── label.tsx       # ✅
│   │   └── badge.tsx       # ✅
│   ├── Navigation.tsx      # Main navigation ✅
│   └── ProtectedLayout.tsx # Auth wrapper ✅
├── lib/
│   ├── api.ts              # API client with all endpoints ✅
│   └── utils.ts            # Utility functions ✅
└── store/
    └── authStore.ts        # Auth state management ✅
```

## ✅ Completed Features

- [x] Project setup with Next.js 14
- [x] Tailwind CSS configuration
- [x] API client with axios interceptors
- [x] Authentication flow with JWT
- [x] Zustand store for auth state
- [x] Protected route wrapper
- [x] Main navigation component
- [x] Login page
- [x] Dashboard page with stats and recent deployments
- [x] Reusable UI components (Button, Card, Input, Label, Badge)

## 🔲 Pages to Complete

The following pages need to be created. I'll provide you with the implementation:

### 1. Applications Page (`app/applications/page.tsx`)
- List all applications
- Create new application (Admin only)
- Delete application (Admin only)
- View application instances

### 2. Database Instances Page (`app/instances/page.tsx`)
- List all database instances grouped by application
- Create new instance (Admin only)
- Test database connection
- Update/delete instances (Admin only)
- Show current patch number

### 3. Deploy Page (`app/deploy/page.tsx`)
- Select database instance
- Upload SQL Scripts ZIP file
- Preview deployment details
- Execute deployment
- Download backup file
- Real-time deployment progress

### 4. History Page (`app/history/page.tsx`)
- List all deployments with pagination
- Filter by application/instance
- View deployment details (patches, procedures, functions)
- Download backup files
- Show deployment duration and status

### 5. Schema Comparison Page (`app/compare/page.tsx`)
- Select source and target database instances
- Compare schemas (tables, procedures, functions)
- Display git-like diff view
- Highlight added, removed, modified objects
- Export comparison results

## 🛠️ Setup Instructions

### 1. Install Dependencies

Already done! Dependencies installed:
- axios
- zustand
- lucide-react
- class-variance-authority
- clsx
- tailwind-merge

### 2. Environment Configuration

Create `.env.local` file:

```env
NEXT_PUBLIC_API_URL=https://localhost:7001/api
```

**Note:** Update the API URL to match your backend server.

### 3. Run Development Server

```bash
npm run dev
```

The app will be available at `http://localhost:3000`

### 4. Build for Production

```bash
npm run build
npm start
```

## 🎯 How to Complete the Frontend

I've built the foundation. To complete the remaining pages, you can:

1. **Use the Dashboard page as a template** - It shows:
   - How to use ProtectedLayout
   - How to call API methods
   - How to display loading states
   - How to use UI components

2. **API Methods are Ready** - All endpoints are in `lib/api.ts`:
   - `api.getApplications()`
   - `api.createApplication(data)`
   - `api.getDatabaseInstances()`
   - `api.deploy(instanceId, file)`
   - `api.compareSchemas(sourceId, targetId)`
   - And more...

3. **UI Components are Ready** - Use them consistently:
   - `<Card>` for containers
   - `<Button>` for actions
   - `<Input>` for forms
   - `<Badge>` for status indicators

## 📝 Quick Implementation Guide

### Example: Create Applications Page

```typescript
'use client';

import { useEffect, useState } from 'react';
import ProtectedLayout from '@/components/ProtectedLayout';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import api, { Application } from '@/lib/api';

export default function ApplicationsPage() {
  const [applications, setApplications] = useState<Application[]>([]);

  useEffect(() => {
    loadApps();
  }, []);

  const loadApps = async () => {
    const data = await api.getApplications();
    setApplications(data);
  };

  return (
    <ProtectedLayout>
      <div className="space-y-6">
        <div className="flex justify-between">
          <h1 className="text-3xl font-bold">Applications</h1>
          <Button>Create Application</Button>
        </div>
        {/* Display applications... */}
      </div>
    </ProtectedLayout>
  );
}
```

## 🔐 Authentication Flow

1. User visits any page
2. `ProtectedLayout` checks for token
3. If no token → redirect to `/login`
4. User logs in → token stored in localStorage
5. Token automatically added to all API requests via axios interceptor
6. If 401 response → auto logout and redirect to login

## 🎨 UI/UX Features

- **Responsive Design** - Works on mobile, tablet, desktop
- **Loading States** - Show loading indicators for async operations
- **Error Handling** - Display error messages to users
- **Status Badges** - Color-coded badges for deployment status
- **Navigation** - Active route highlighting
- **Protected Routes** - Auth-required pages automatically protected

## 🚀 Next Steps

1. Complete the 5 remaining pages using the patterns established
2. Add form validation for create/edit forms
3. Add confirmation dialogs for delete operations
4. Add toast notifications for success/error messages
5. Optionally add SignalR for real-time deployment logs

## 📚 Resources

- **API Documentation:** See `API/README.md`
- **Backend Source:** See `API/` directory
- **Design System:** Tailwind CSS + Custom Components

---

**Status:** Foundation Complete ✅  
**Ready for:** Page Implementation

The infrastructure is solid - now just build out the remaining CRUD pages following the established patterns!
