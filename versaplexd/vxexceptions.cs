/*
 * Versaplex:
 *   Copyright (C)2007-2008 Versabanq Innovations Inc. and contributors.
 *       See the included file named LICENSE for license information.
 */
using System;
using System.Data.Common;
using System.Data.SqlClient;
using System.Runtime.Serialization;

class VxRequestException : Exception {
    public string DBusErrorType;

    public VxRequestException(string errortype)
        : base()
    {
        DBusErrorType = errortype;
    }
    
    public VxRequestException(string errortype, string msg)
        : base(msg)
    {
        DBusErrorType = errortype;
    }

    public VxRequestException(string errortype, SerializationInfo si, 
            StreamingContext sc)
        : base(si, sc)
    {
        DBusErrorType = errortype;
    }

    public VxRequestException(string errortype, string msg, Exception inner)
        : base(msg, inner)
    {
        DBusErrorType = errortype;
    }
}

class VxSqlException : VxRequestException {
    public VxSqlException()
        : base("vx.db.sqlerror")
    {
    }
    
    public VxSqlException(string msg)
        : base("vx.db.sqlerror", msg)
    {
    }

    public VxSqlException(SerializationInfo si, StreamingContext sc)
        : base("vx.db.sqlerror", si, sc)
    {
    }

    public VxSqlException(string msg, Exception inner)
        : base("vx.db.sqlerror", msg, inner)
    {
    }

    public bool ContainsSqlError(int errno)
    {
        if (!(InnerException is SqlException))
            return false;

        SqlException sqle = (SqlException)InnerException;
        foreach (SqlError err in sqle.Errors)
        {
            if (err.Number == errno)
                return true;
        }
        return false;
    }

    // Returns the SQL error number of the first SQL Exception in the list, or
    // -1 if none can be found.
    public int Number
    {
        get
        {
            if (!(InnerException is SqlException))
                return -1;

            SqlException sqle = (SqlException)InnerException;
            return sqle.Number;
        }
    }
}

class VxTooMuchDataException : VxRequestException {
    public VxTooMuchDataException()
        : base("vx.db.toomuchdata")
    {
    }
    
    public VxTooMuchDataException(string msg)
        : base("vx.db.toomuchdata", msg)
    {
    }

    public VxTooMuchDataException(SerializationInfo si, StreamingContext sc)
        : base("vx.db.toomuchdata", si, sc)
    {
    }

    public VxTooMuchDataException(string msg, Exception inner)
        : base("vx.db.toomuchdata", msg, inner)
    {
    }
}

class VxBadSchemaException : VxRequestException {
    public VxBadSchemaException()
        : base("vx.db.badschema")
    {
    }
    
    public VxBadSchemaException(string msg)
        : base("vx.db.badschema", msg)
    {
    }

    public VxBadSchemaException(SerializationInfo si, StreamingContext sc)
        : base("vx.db.badschema", si, sc)
    {
    }

    public VxBadSchemaException(string msg, Exception inner)
        : base("vx.db.badschema", msg, inner)
    {
    }
}

class VxConfigException : VxRequestException {
    public VxConfigException()
        : base("vx.db.configerror")
    {
    }
    
    public VxConfigException(string msg)
        : base("vx.db.configerror", msg)
    {
    }

    public VxConfigException(SerializationInfo si, StreamingContext sc)
        : base("vx.db.configerror", si, sc)
    {
    }

    public VxConfigException(string msg, Exception inner)
        : base("vx.db.configerror", msg, inner)
    {
    }
}

class VxSecurityException : VxRequestException {
    public VxSecurityException()
	: base("vx.db.securityerror")
    {
    }

    public VxSecurityException(string msg)
	: base("vx.db.securityerror", msg)
    {
    }
}
